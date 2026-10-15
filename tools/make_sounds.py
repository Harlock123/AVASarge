#!/usr/bin/env python3
"""Synthesises AVASarge's sound effects and tunes into ../src/AVASarge/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASarge", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def snare(seconds=0.12, gain=1.0):
    return mix(tone(seconds, 3000, 1500, "noise", attack=0.001, release=1.4, gain=gain), tone(seconds * 0.6, 220, 160, "tri", release=1.2, gain=gain * 0.5))


def kick(seconds=0.14, gain=1.0):
    return tone(seconds, 120, 45, "sine", attack=0.001, release=1.2, gain=gain)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The guns: yours a sharp crack, theirs a duller thump.
    write("Fire.wav", mix(tone(0.25, 2400, 200, "noise", attack=0.001, release=1.0), tone(0.2, 180, 60, "square", release=1.0, gain=0.6)), 0.4)
    write("EnemyFire.wav", mix(tone(0.25, 1500, 150, "noise", attack=0.001, release=1.0, gain=0.8), tone(0.2, 130, 50, "square", release=1.0, gain=0.6)), 0.34)
    # Hits: a clang on armour, a tank going up, bricks and rock.
    write("Hit.wav", mix(tone(0.3, 900, 860, "square", release=0.9, vibrato=0.04, vib_rate=40), tone(0.15, 4000, 2000, "noise", release=1.0, gain=0.4)), 0.36)
    write("Boom.wav", mix(tone(1.2, 2600, 60, "noise", release=0.6), tone(1.0, 110, 30, "saw", release=0.7, gain=0.8),
                          cat(silence(0.15), tone(0.6, 1200, 300, "noise", release=0.8, gain=0.4))), 0.55)
    write("Wall.wav", mix(tone(0.25, 1400, 300, "noise", release=0.8), tone(0.12, 300, 150, "square", release=0.9, gain=0.4)), 0.34)
    write("Rock.wav", mix(tone(0.2, 900, 250, "noise", release=0.9), tone(0.1, 200, 120, "tri", release=0.9, gain=0.5)), 0.3)
    write("Rumble.wav", mix(tone(0.9, 600, 80, "noise", release=0.5), tone(0.9, 70, 40, "saw", release=0.5, gain=0.6)), 0.42)
    write("Point.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["G5", "C6", "E6"]]), 0.36)
    write("Death.wav", cat(mix(tone(1.0, 2400, 60, "noise", release=0.6), tone(0.9, 140, 30, "saw", release=0.6, gain=0.8)),
                           *[tone(0.16, hz(n), kind="tri", release=0.4) for n in ["G4", "F4", "Eb4", "D4"]]), 0.5)
    # Bugle calls: to arms, and the round won.
    write("RoundStart.wav", seq(150, [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 0.5), ("G5", 1.5)], kind="square", gain=0.5, release=0.3), 0.42)
    write("Clear.wav", seq(150, [("C5", 0.33), ("C5", 0.33), ("C5", 0.33), ("E5", 1), ("C5", 0.5), ("E5", 0.5), ("G5", 2)], kind="square", gain=0.5, release=0.3), 0.44)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7", "G6", "C7"]]), 0.4)

    # The battle's tune: a marching snare and kick under a low brass line in C minor (new). Loops.
    bpm = 120
    beat = 60.0 / bpm
    drums = []
    for bar in range(8):
        for b in range(4):
            hit = mix(kick(0.18), snare(0.1, 0.7)) if b % 2 == 0 else snare(0.12)
            drums.extend(hit + silence(beat / 2 - len(hit) / RATE))
            drums.extend(snare(0.06, 0.45) + silence(beat / 2 - 0.06))
    brass = seq(bpm, [("C3", 1.5), ("C3", 0.5), ("Eb3", 1), ("G3", 1), ("F3", 1.5), ("Eb3", 0.5), ("D3", 2),
                      ("C3", 1.5), ("C3", 0.5), ("Eb3", 1), ("G3", 1), ("Ab3", 1.5), ("G3", 0.5), ("F3", 2),
                      ("G3", 1.5), ("G3", 0.5), ("Ab3", 1), ("G3", 1), ("F3", 1.5), ("Eb3", 0.5), ("D3", 2),
                      ("Eb3", 1), ("D3", 1), ("C3", 1), ("B2", 1), ("C3", 4)], kind="saw", gain=0.35, release=0.4)
    write("Music.wav", mix(drums, brass), 0.4)

    # The title: a proud march (new), with drums.
    lead = [("G4", 0.75), ("G4", 0.25), ("C5", 1), ("C5", 0.5), ("D5", 0.5), ("E5", 1), ("C5", 1),
            ("D5", 0.75), ("D5", 0.25), ("G5", 1), ("F5", 0.5), ("E5", 0.5), ("D5", 2),
            ("E5", 0.75), ("E5", 0.25), ("A5", 1), ("G5", 0.5), ("F5", 0.5), ("E5", 1), ("C5", 1),
            ("D5", 1), ("G4", 0.5), ("B4", 0.5), ("C5", 2), ("-", 2)]
    bass = [("C3", 2), ("G2", 2), ("C3", 2), ("C3", 2), ("G2", 2), ("D3", 2), ("G2", 2), ("G2", 2),
            ("A2", 2), ("F2", 2), ("C3", 2), ("A2", 2), ("G2", 2), ("G2", 2), ("C3", 4)]
    music = tune(116, lead, bass, kind="square")
    beat = 60.0 / 116
    drums = []
    while len(drums) < len(music):
        drums.extend(kick(0.16, 0.8) + silence(beat - 0.16))
        drums.extend(snare(0.1, 0.6) + silence(beat - 0.1))
    write("TitleTune.wav", mix(music, drums[:len(music)]), 0.42)
    write("StartTune.wav", seq(160, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1.5)], kind="square", gain=0.5, release=0.3), 0.45)
    write("GameOver.wav", tune(70, [("G4", 1), ("Eb4", 1), ("C4", 1), ("D4", 1), ("B3", 1), ("C4", 3)], [("C3", 2), ("Ab2", 2), ("G2", 2), ("C3", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
