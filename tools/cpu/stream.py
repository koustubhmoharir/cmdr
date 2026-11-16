#!/usr/bin/env python3
"""Streaming output for the CPU measurement kit (see README.md).

usage: stream.py [rate] [--progress] [--seed N]
  rate        lines per second in line mode (default 100)
  --progress  draw a progress bar redrawn in place about 20 times a second, with a
              newline about every 5 s (rate is ignored)
  --seed N    random seed (default 1); the same seed gives the same text
"""
import argparse
import math
import random
import sys
import time

ALPHA = "abcdefghijklmnopqrstuvwxyz"
COLORS = ["\x1b[31m", "\x1b[32m", "\x1b[33m", "\x1b[34m", "\x1b[35m", "\x1b[36m"]
RESET = "\x1b[0m"
BAR_WIDTH = 20
PROGRESS_PERIOD = 0.05  # 20 updates per second
NEWLINE_EVERY = 5.0
TOTAL_BYTES = 1 << 30  # simulated transfer size; the bar wraps around when it completes


def word(rng):
    return "".join(rng.choice(ALPHA) for _ in range(rng.randint(1, 12)))


def line(i, rng):
    target = rng.randint(20, 150)
    colored = rng.random() < 0.30
    n_col = rng.randint(1, 3) if colored else 0
    words = []
    length = len(f"{i:08d} ")
    while length < target:
        words.append(word(rng))
        length += len(words[-1]) + 1
    # mark n_col random word indices to be colored
    idx = set(rng.sample(range(len(words)), min(n_col, len(words))))
    parts = []
    for k, w in enumerate(words):
        if k in idx:
            parts.append(rng.choice(COLORS) + w + RESET)
        else:
            parts.append(w)
    return f"{i:08d} " + " ".join(parts)


def paced(period, step):
    """Call step() every period seconds, sleeping until each due time (no drift)."""
    next_t = time.monotonic()
    while True:
        step()
        next_t += period
        d = next_t - time.monotonic()
        if d > 0:
            time.sleep(d)
        elif d < -1.0:
            next_t = time.monotonic()  # far behind: do not burst to catch up


def run_lines(rate, rng):
    out = sys.stdout
    counter = [0]

    def step():
        out.write(line(counter[0], rng) + "\n")
        out.flush()
        counter[0] += 1

    paced(1.0 / rate, step)


def run_progress(rng):
    out = sys.stdout
    start = time.monotonic()
    state = {"done": 0.0, "last": time.monotonic(), "nl_at": time.monotonic()}

    def step():
        now = time.monotonic()
        dt = now - state["last"]
        state["last"] = now
        t = now - start
        speed = 12.0 + 3.0 * math.sin(t / 2.0)  # MB/s, varies smoothly
        state["done"] += speed * 1e6 * dt
        if state["done"] >= TOTAL_BYTES:
            state["done"] = 0.0
        pct = int(100 * state["done"] / TOTAL_BYTES)
        filled = pct * BAR_WIDTH // 100
        text = "[%s%s] %3d%%  %4.1f MB/s" % ("#" * filled, " " * (BAR_WIDTH - filled), pct, speed)
        if now - state["nl_at"] >= NEWLINE_EVERY:
            out.write("\r" + text + "\n")
            state["nl_at"] = now
        else:
            out.write("\r" + text)
        out.flush()

    paced(PROGRESS_PERIOD, step)


def main():
    ap = argparse.ArgumentParser(description="Streaming output for CPU measurements.")
    ap.add_argument("rate", nargs="?", type=float, default=100.0, help="lines per second (default 100)")
    ap.add_argument("--progress", action="store_true", help="progress bar instead of lines")
    ap.add_argument("--seed", type=int, default=1, help="random seed (default 1)")
    a = ap.parse_args()
    if a.rate <= 0:
        ap.error("rate must be greater than 0")
    rng = random.Random(a.seed)
    try:
        if a.progress:
            run_progress(rng)
        else:
            run_lines(a.rate, rng)
    except (BrokenPipeError, KeyboardInterrupt):
        pass


if __name__ == "__main__":
    main()
