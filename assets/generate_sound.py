"""Regenerate the included alarm.wav using only Python's standard library."""
import math
import struct
import wave
from pathlib import Path

rate = 22050
samples = bytearray()
for i in range(rate * 3):
    phase = i % 11025
    if phase < 8820:
        envelope = min(1, phase / 220) * min(1, (8819 - phase) / 220)
        frequency = 880 if (i // 11025) % 2 == 0 else 660
        value = int(18000 * envelope * math.sin(2 * math.pi * frequency * i / rate))
    else:
        value = 0
    samples.extend(struct.pack("<h", value))
with wave.open(str(Path(__file__).resolve().parent / "alarm.wav"), "wb") as stream:
    stream.setnchannels(1)
    stream.setsampwidth(2)
    stream.setframerate(rate)
    stream.writeframes(samples)

