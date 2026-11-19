import sys, os, json, time
import av
import numpy as np
from faster_whisper import WhisperModel


def emit(obj):
    sys.stdout.write(json.dumps(obj, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def decode(path, rate=16000):
    with av.open(path) as container:
        if not container.streams.audio:
            raise RuntimeError("文件里没有音频轨")
        stream = container.streams.audio[0]
        res = av.AudioResampler(format="fltp", layout="mono", rate=rate)
        parts = []
        for frame in container.decode(stream):
            for rf in res.resample(frame):
                parts.append(rf.to_ndarray())
        for rf in res.resample(None):
            parts.append(rf.to_ndarray())
    if not parts:
        return np.zeros(0, dtype=np.float32)
    return np.concatenate([p.reshape(-1) for p in parts]).astype(np.float32)


def main():
    if len(sys.argv) < 3:
        emit({"type": "error", "message": "参数不足：asr_transcribe.py <模型目录> <音频文件> [语言]"})
        return 2
    model_dir = sys.argv[1]
    audio = sys.argv[2]
    lang = sys.argv[3] if len(sys.argv) > 3 else "zh"
    if lang in ("", "auto"):
        lang = None
    if not os.path.isdir(model_dir):
        emit({"type": "error", "message": "模型目录不存在：" + model_dir})
        return 2
    if not os.path.isfile(audio):
        emit({"type": "error", "message": "音频文件不存在：" + audio})
        return 2

    try:
        model = WhisperModel(model_dir, device="cpu", compute_type="int8", cpu_threads=8)
    except Exception as e:
        emit({"type": "error", "message": "模型加载失败：" + str(e)})
        return 3

    try:
        pcm = decode(audio)
    except Exception as e:
        emit({"type": "error", "message": "音频解码失败：" + str(e)})
        return 4
    if pcm.size == 0:
        emit({"type": "error", "message": "没有解码出音频数据"})
        return 4

    t0 = time.time()
    try:
        segments, info = model.transcribe(pcm, language=lang, beam_size=5, vad_filter=True)
        emit({"type": "start", "dur": round(info.duration, 2), "lang": info.language})
        count = 0
        for s in segments:
            text = (s.text or "").strip()
            if not text:
                continue
            emit({"type": "seg", "start": round(s.start, 2), "end": round(s.end, 2), "text": text})
            count += 1
        emit({"type": "done", "dur": round(info.duration, 2),
              "run": round(time.time() - t0, 2), "count": count})
    except Exception as e:
        emit({"type": "error", "message": "转写失败：" + str(e)})
        return 5
    return 0


if __name__ == "__main__":
    sys.exit(main())
