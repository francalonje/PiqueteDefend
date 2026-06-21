"""Genera clicks de UI sintéticos (WAV PCM 16-bit mono), libres de regalías.
Un click = seno con decaimiento exponencial + algo de ruido para el 'snap'."""
import wave, struct, math, random

SR = 44100

def write_click(path, freq, dur, decay, amp=0.5, noise=0.0, seed=0):
    random.seed(seed)
    n = int(SR * dur)
    frames = bytearray()
    for i in range(n):
        t = i / SR
        env = math.exp(-t / decay)
        atk = min(1.0, t / 0.0008)          # ataque corto para evitar 'pop'
        s = math.sin(2 * math.pi * freq * t)
        if noise > 0:
            s = (1 - noise) * s + noise * random.uniform(-1, 1)
        val = amp * atk * env * s
        v = int(max(-1.0, min(1.0, val)) * 32767)
        frames += struct.pack('<h', v)
    with wave.open(path, 'w') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(bytes(frames))
    print(f"  {path}  ({n} samples, {dur*1000:.0f} ms)")

base = r"D:\PiqueteDefend\PiqueteDefend\Assets\PiqueteDefend\Presentation\Resources\Audio"
print("Generando clicks:")
write_click(base + r"\button-click.wav", freq=1200, dur=0.045, decay=0.010, amp=0.45, noise=0.15, seed=1)
write_click(base + r"\card-click.wav",   freq=1900, dur=0.035, decay=0.007, amp=0.45, noise=0.28, seed=2)
# Default global al JUGAR una carta: click medio, un poco más grave que el de agarrar.
write_click(base + r"\card-play.wav",    freq=520,  dur=0.070, decay=0.018, amp=0.50, noise=0.20, seed=3)
# Default global de golpe (ataque): thud grave y ruidoso → suena a impacto.
write_click(base + r"\attack-hit.wav",   freq=150,  dur=0.140, decay=0.050, amp=0.70, noise=0.55, seed=4)
print("Listo.")
