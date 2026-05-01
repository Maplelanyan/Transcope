import argparse
import contextlib
import json
import os
import sys


os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")
os.environ.setdefault("GLOG_minloglevel", "2")
os.environ.setdefault("FLAGS_use_mkldnn", "0")
os.environ.setdefault("FLAGS_enable_pir_api", "0")
os.environ.setdefault("PYTHONIOENCODING", "utf-8")
os.environ.setdefault("PYTHONUTF8", "1")

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")


def normalize_device(device):
    if not device:
        return "cpu"

    lowered = str(device).strip().lower()
    if lowered == "gpu":
        return "gpu:0"

    return lowered


def ensure_runtime_support(device):
    import paddle

    if device.startswith("gpu"):
        if not paddle.device.is_compiled_with_cuda():
            raise RuntimeError(
                "GPU mode requires a CUDA-enabled PaddlePaddle build. "
                "Install a GPU wheel and use a Python environment that has it."
            )

        paddle.device.set_device(device)
        return

    paddle.device.set_device("cpu")


def build_ocr(language, device):
    from paddleocr import PaddleOCR

    normalized_device = normalize_device(device)
    ensure_runtime_support(normalized_device)

    constructor_attempts = [
        {
            "lang": language,
            "device": normalized_device,
            "use_doc_orientation_classify": False,
            "use_doc_unwarping": False,
            "use_textline_orientation": False,
        },
        {
            "lang": language,
            "device": normalized_device,
            "use_doc_orientation_classify": False,
            "use_doc_unwarping": False,
            "use_angle_cls": False,
        },
        {"lang": language, "device": normalized_device, "use_angle_cls": True},
        {"lang": language, "device": normalized_device},
    ]

    last_error = None
    for kwargs in constructor_attempts:
        try:
            return PaddleOCR(**kwargs)
        except TypeError as error:
            last_error = error

    raise last_error


def run_prediction(ocr, image_path):
    if hasattr(ocr, "predict"):
        return ocr.predict(
            input=image_path,
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
        )

    return ocr.ocr(image_path, cls=True)


def normalize_results(raw_results):
    items = []

    for result in ensure_list(raw_results):
        if isinstance(result, dict):
            items.extend(normalize_dict_result(result))
            continue

        json_result = getattr(result, "json", None)
        if callable(json_result):
            try:
                parsed = json_result
                if not isinstance(parsed, dict):
                    parsed = json.loads(json_result())
                items.extend(normalize_dict_result(parsed))
                continue
            except Exception:
                pass

        items.extend(normalize_legacy_result(result))

    return items


def normalize_dict_result(result):
    data = result.get("res", result)
    texts = data.get("rec_texts") or data.get("texts") or []
    scores = data.get("rec_scores") or data.get("scores") or []
    boxes = (
        data.get("rec_polys")
        or data.get("dt_polys")
        or data.get("rec_boxes")
        or data.get("boxes")
        or []
    )

    items = []
    for index, text in enumerate(texts):
        if not text or not str(text).strip():
            continue

        box = boxes[index] if index < len(boxes) else None
        normalized_box = normalize_box(box)
        if normalized_box is None:
            continue

        confidence = scores[index] if index < len(scores) else None
        items.append(
            {
                "text": str(text).strip(),
                "confidence": to_float_or_none(confidence),
                "box": normalized_box,
            }
        )

    return items


def normalize_legacy_result(result):
    items = []

    for line in flatten_legacy_lines(result):
        if not isinstance(line, (list, tuple)) or len(line) < 2:
            continue

        box = normalize_box(line[0])
        text = None
        confidence = None

        text_result = line[1]
        if isinstance(text_result, (list, tuple)) and len(text_result) >= 1:
            text = text_result[0]
            if len(text_result) >= 2:
                confidence = to_float_or_none(text_result[1])
        else:
            text = text_result

        if box is None or not text or not str(text).strip():
            continue

        items.append(
            {
                "text": str(text).strip(),
                "confidence": confidence,
                "box": box,
            }
        )

    return items


def flatten_legacy_lines(result):
    if result is None:
        return []

    if (
        isinstance(result, (list, tuple))
        and len(result) == 1
        and isinstance(result[0], (list, tuple))
    ):
        return flatten_legacy_lines(result[0])

    lines = []
    if isinstance(result, (list, tuple)):
        for item in result:
            if is_legacy_line(item):
                lines.append(item)
            elif isinstance(item, (list, tuple)):
                lines.extend(flatten_legacy_lines(item))

    return lines


def is_legacy_line(value):
    return (
        isinstance(value, (list, tuple))
        and len(value) >= 2
        and isinstance(value[0], (list, tuple))
    )


def normalize_box(box):
    if box is None:
        return None

    if hasattr(box, "tolist"):
        box = box.tolist()

    if not isinstance(box, (list, tuple)):
        return None

    if len(box) == 4 and all(is_number(value) for value in box):
        left, top, right, bottom = [float(value) for value in box]
        return [[left, top], [right, top], [right, bottom], [left, bottom]]

    if len(box) < 4:
        return None

    points = []
    for point in box[:4]:
        if hasattr(point, "tolist"):
            point = point.tolist()
        if not isinstance(point, (list, tuple)) or len(point) < 2:
            return None
        points.append([float(point[0]), float(point[1])])

    return points


def ensure_list(value):
    if value is None:
        return []
    if isinstance(value, list):
        return value
    if isinstance(value, tuple):
        return list(value)
    return [value]


def is_number(value):
    try:
        float(value)
        return True
    except (TypeError, ValueError):
        return False


def to_float_or_none(value):
    if value is None:
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--image")
    parser.add_argument("--lang", default="ch")
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--server", action="store_true")
    args = parser.parse_args()

    try:
        with contextlib.redirect_stdout(sys.stderr):
            ocr = build_ocr(args.lang, args.device)

        if args.server:
            print(json.dumps({"ok": True, "ready": True}, ensure_ascii=False), flush=True)

            for line in sys.stdin:
                payload = line.strip()
                if not payload:
                    continue

                try:
                    request = json.loads(payload)
                    command = request.get("command")

                    if command == "exit":
                        print(json.dumps({"ok": True, "bye": True}, ensure_ascii=False), flush=True)
                        return 0

                    if command != "recognize":
                        raise ValueError(f"Unsupported command: {command}")

                    image_path = request.get("image")
                    if not image_path:
                        raise ValueError("Missing image path.")

                    with contextlib.redirect_stdout(sys.stderr):
                        raw_results = run_prediction(ocr, image_path)

                    print(
                        json.dumps(
                            {"ok": True, "items": normalize_results(raw_results)},
                            ensure_ascii=False,
                        ),
                        flush=True,
                    )
                except Exception as error:
                    print(
                        json.dumps(
                            {"ok": False, "items": [], "error": str(error)},
                            ensure_ascii=False,
                        ),
                        flush=True,
                    )
            return 0

        if args.check:
            print(json.dumps({"ok": True, "items": []}, ensure_ascii=False))
            return 0

        if not args.image:
            raise ValueError("--image is required unless --check is set.")

        with contextlib.redirect_stdout(sys.stderr):
            raw_results = run_prediction(ocr, args.image)
        print(
            json.dumps(
                {"ok": True, "items": normalize_results(raw_results)},
                ensure_ascii=False,
            )
        )
        return 0
    except Exception as error:
        print(
            json.dumps({"ok": False, "items": [], "error": str(error)}, ensure_ascii=False)
        )
        return 1


if __name__ == "__main__":
    sys.exit(main())
