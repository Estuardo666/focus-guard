import { Children, useCallback, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { ChevronLeftIcon, ChevronRightIcon, InfoCircledIcon, MinusIcon, PlusIcon } from '@radix-ui/react-icons';

/**
 * Secondary explanations live behind a small "i": shown on hover, keyboard focus or tap, dismissed with Escape. The text
 * stays in the DOM as the trigger's description, so screen readers still announce it.
 */
export function InfoTip({ label, text }: { label: string; text: string }) {
  const id = useId();
  const [open, setOpen] = useState(false);
  return <span className="info-tip" data-open={open}>
    <button type="button" className="info-tip-trigger" aria-label={label} aria-describedby={id} aria-expanded={open}
      onClick={() => setOpen(value => !value)} onBlur={() => setOpen(false)}
      onKeyDown={event => { if (event.key === 'Escape' && open) { event.preventDefault(); event.stopPropagation(); setOpen(false); } }}>
      <InfoCircledIcon aria-hidden="true" focusable="false"/>
    </button>
    <span role="tooltip" id={id} className="info-tip-bubble">{text}</span>
  </span>;
}

/** A − value + control around a real number input, so typing, arrows and assistive technology keep working. */
export function Stepper({ id, value, min, max, step = 1, onChange, disabled = false, unit, decrease, increase, label, describedBy }: {
  id?: string; describedBy?: string; value: string; min: number; max: number; step?: number; onChange(value: string): void; disabled?: boolean;
  unit?: ReactNode; decrease: string; increase: string; label?: string;
}) {
  const current = Number(value);
  const valid = value.trim() !== '' && Number.isFinite(current);
  const nudge = (direction: 1 | -1) => {
    const base = valid ? current : min;
    // Snap to the step grid first (24 → 25 with step 5), then move.
    const snapped = direction > 0 ? Math.floor(base / step) * step + step : Math.ceil(base / step) * step - step;
    onChange(String(Math.min(max, Math.max(min, snapped))));
  };
  return <span className="stepper" data-invalid={!valid || current < min || current > max ? 'true' : undefined}>
    <button type="button" className="stepper-button" aria-label={decrease} disabled={disabled || (valid && current <= min)} onClick={() => nudge(-1)}><MinusIcon aria-hidden="true" focusable="false"/></button>
    <input id={id} type="number" inputMode="numeric" min={min} max={max} step="any" value={value} disabled={disabled} aria-label={label} aria-describedby={describedBy}
      onChange={event => onChange(event.target.value)}/>
    {unit && <span className="stepper-unit" aria-hidden="true">{unit}</span>}
    <button type="button" className="stepper-button" aria-label={increase} disabled={disabled || (valid && current >= max)} onClick={() => nudge(1)}><PlusIcon aria-hidden="true" focusable="false"/></button>
  </span>;
}

/**
 * Horizontal card rail without a visible scrollbar: arrows move one card, dots show where you are and jump to a card.
 * The cards stay a native radio group, so arrow keys and screen readers work as before.
 */
export function CardSlider({ className, labelledBy, previous, next, goTo, children, groupRole = 'radiogroup' }: {
  className?: string; labelledBy: string; previous: string; next: string; goTo(index: number): string; children: ReactNode; groupRole?: 'radiogroup' | 'group';
}) {
  const rail = useRef<HTMLDivElement>(null);
  const count = Children.count(children);
  const [index, setIndex] = useState(0);
  const [overflow, setOverflow] = useState(false);
  const [edges, setEdges] = useState({ start: true, end: false });
  const measure = useCallback(() => {
    const element = rail.current;
    if (!element) return;
    setOverflow(element.scrollWidth > element.clientWidth + 2);
    const card = element.firstElementChild as HTMLElement | null;
    const step = card ? card.offsetWidth + 10 : element.clientWidth;
    const atEnd = element.scrollLeft + element.clientWidth >= element.scrollWidth - 2;
    setIndex(atEnd ? count - 1 : Math.round(element.scrollLeft / Math.max(1, step)));
    setEdges({ start: element.scrollLeft <= 2, end: atEnd });
  }, [count]);
  useEffect(() => {
    const element = rail.current;
    if (!element) return;
    measure();
    let frame = 0;
    const onScroll = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(measure); };
    element.addEventListener('scroll', onScroll, { passive: true });
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure);
    observer?.observe(element);
    return () => { cancelAnimationFrame(frame); element.removeEventListener('scroll', onScroll); observer?.disconnect(); };
  }, [measure]);
  // Arrows move one card from where the rail is now; jumping to a card index would not move when several cards fit.
  const scrollByCard = (direction: 1 | -1) => {
    const element = rail.current;
    const card = element?.firstElementChild as HTMLElement | null;
    if (element) element.scrollBy({ left: direction * (card ? card.offsetWidth + 10 : element.clientWidth * 0.8), behavior: 'smooth' });
  };
  const scrollToCard = (target: number) => {
    const element = rail.current;
    const card = element?.children[Math.max(0, Math.min(count - 1, target))] as HTMLElement | undefined;
    if (element && card) element.scrollTo({ left: card.offsetLeft, behavior: 'smooth' });
  };
  return <div className="card-slider" data-overflow={overflow}>
    <div ref={rail} className={`choice-cards ${className ?? ''}`} role={groupRole} aria-labelledby={labelledBy}>{children}</div>
    {overflow && <div className="card-slider-controls">
      <button type="button" className="card-slider-arrow" aria-label={previous} disabled={edges.start} onClick={() => scrollByCard(-1)}><ChevronLeftIcon aria-hidden="true" focusable="false"/></button>
      <div className="card-slider-dots">{Array.from({ length: count }, (_, dot) => <button key={dot} type="button" className="card-slider-dot" aria-label={goTo(dot + 1)} aria-current={dot === index ? 'true' : undefined} onClick={() => scrollToCard(dot)}/>)}</div>
      <button type="button" className="card-slider-arrow" aria-label={next} disabled={edges.end} onClick={() => scrollByCard(1)}><ChevronRightIcon aria-hidden="true" focusable="false"/></button>
    </div>}
  </div>;
}

const classicPresetId = 'a2af4f65-2de8-4e89-a0bc-4c4db6e24a01';
const deepFocusPresetId = 'a2af4f65-2de8-4e89-a0bc-4c4db6e24a02';
const extendedFlowPresetId = 'a2af4f65-2de8-4e89-a0bc-4c4db6e24a03';
/** A recognisable glyph for a cycle preset: the built-ins by id, otherwise by a few name hints, else none (plain orb). */
export function presetGlyph(id: string, name: string): string | null {
  const key = id.toLowerCase();
  if (key === classicPresetId || /pomodoro|tomate|tomato/i.test(name)) return '🍅';
  if (key === deepFocusPresetId || /profund|deep/i.test(name)) return '🌊';
  if (key === extendedFlowPresetId || /flujo|flow/i.test(name)) return '🌀';
  if (/sprint|r[aá]pid|quick/i.test(name)) return '⚡';
  return null;
}

const dialogExitMs = 170;
/**
 * Plays a dialog's exit (fade, slight scale and its backdrop) and only then runs `after`, which closes or unmounts it.
 * Reduced motion and environments without Web Animations (tests) close at once.
 */
export function exitDialog(element: HTMLDialogElement | null | undefined, after: () => void) {
  const reduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches || document.documentElement.dataset.reduceMotion === 'true';
  if (!element?.open || reduced || typeof element.animate !== 'function') { after(); return; }
  if (element.dataset.closing) return;
  element.dataset.closing = 'true';
  window.setTimeout(() => {
    after();
    // Keep the faded state until React has unmounted or closed it (two frames), so it never flashes back in.
    requestAnimationFrame(() => requestAnimationFrame(() => { delete element.dataset.closing; }));
  }, dialogExitMs);
}
