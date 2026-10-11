import { createDecorativeFrameLoop } from './decorative-frame-loop';

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

it('bounds decorative drawing and releases all scheduled work when hidden or disposed', () => {
  let visible = true;
  const draw = vi.fn();
  const frames = createDecorativeFrameLoop(draw, () => visible);
  frames.sync();
  vi.advanceTimersByTime(1000);
  expect(draw.mock.calls.length).toBeGreaterThan(15);
  expect(draw.mock.calls.length).toBeLessThanOrEqual(30);
  visible = false;
  frames.sync();
  const count = draw.mock.calls.length;
  expect(vi.getTimerCount()).toBe(0);
  vi.advanceTimersByTime(10_000);
  expect(draw).toHaveBeenCalledTimes(count);
  visible = true;
  frames.sync();
  vi.advanceTimersByTime(100);
  expect(draw.mock.calls.length).toBeGreaterThan(count);
  expect(draw.mock.calls.at(-1)![0]).toBeLessThanOrEqual(0.1);
  frames.dispose();
  frames.sync();
  expect(vi.getTimerCount()).toBe(0);
});

it('does not start a frame loop for reduced motion and repeated invalidations never duplicate it', () => {
  let animate = false;
  const draw = vi.fn();
  const frames = createDecorativeFrameLoop(draw, () => animate);
  frames.sync();
  expect(vi.getTimerCount()).toBe(0);
  animate = true;
  frames.sync(); frames.sync(); frames.sync();
  expect(vi.getTimerCount()).toBe(1);
  animate = false;
  vi.advanceTimersByTime(100);
  expect(draw).not.toHaveBeenCalled();
  expect(vi.getTimerCount()).toBe(0);
  frames.dispose();
});
