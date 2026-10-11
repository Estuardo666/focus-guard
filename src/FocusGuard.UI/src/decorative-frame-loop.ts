/** Slow decorative motion needs at most 30 draws/s. A still or hidden scene has no pending frames/timers. */
export function createDecorativeFrameLoop(draw: (delta: number) => void, canAnimate: () => boolean) {
  let frame = 0;
  let timer = 0;
  let disposed = false;
  let last = performance.now();
  const stop = () => {
    cancelAnimationFrame(frame);
    window.clearTimeout(timer);
    frame = timer = 0;
  };
  const queue = () => {
    if (disposed || !canAnimate() || frame || timer) return;
    timer = window.setTimeout(() => {
      timer = 0;
      if (disposed || !canAnimate()) return;
      frame = requestAnimationFrame(now => {
        frame = 0;
        if (disposed || !canAnimate()) return;
        draw(Math.min(Math.max(0, (now - last) / 1000), 0.1));
        last = now;
        queue();
      });
    }, 1000 / 30);
  };
  return {
    sync() {
      if (disposed) return;
      if (!canAnimate()) stop();
      else { last = performance.now(); queue(); }
    },
    dispose() { disposed = true; stop(); },
  };
}
