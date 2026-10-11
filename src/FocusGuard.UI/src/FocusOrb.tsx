import { lazy, Suspense, useCallback, useEffect, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react';
import { motion, useMotionValue, useReducedMotion, useSpring } from 'framer-motion';
import { isOrbStyle, readOrbColors } from './design-system/palettes';
import type { OrbPointer } from './FocusOrbScene';
import type { RingSegment } from './cycle-schedule';

// A missing or blocked scene chunk degrades to the CSS orb instead of reaching the error boundary.
const FocusOrbScene = lazy(() => import('./FocusOrbScene').catch(() => ({ default: () => <div className="orb-fallback"/> })));

export const dialMaximumMinutes = 120;
const dialStep = 5;
const minimumMinutes = 1;
const maximumMinutes = 720;
// Six breaths a minute: the resting pace used by paced-breathing exercises.
const idleBreathSeconds = 10;
const focusBreathSeconds = 12;

export function supportsWebGL(): boolean {
  // Creating a probe context leaves a second GPU context alive until GC. The scene itself handles creation failure.
  return typeof window !== 'undefined' && typeof WebGL2RenderingContext !== 'undefined';
}

/** Converts a ring angle (0 at the top, clockwise, radians) into a 5-minute step. */
export function minutesForAngle(angle: number): number {
  const turn = ((angle % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2);
  return Math.max(dialStep, Math.round((turn / (Math.PI * 2)) * dialMaximumMinutes / dialStep) * dialStep);
}

function clampMinutes(value: number) {
  return Math.min(maximumMinutes, Math.max(minimumMinutes, Math.round(value)));
}

function useDocumentLook() {
  const read = () => {
    const root = document.documentElement;
    return {
      colors: readOrbColors(root),
      light: root.dataset.theme === 'light',
      reduce: root.dataset.reduceMotion === 'true',
      constrained: root.dataset.resourceConstrained === 'true',
      hidden: document.hidden || root.dataset.hostHidden === 'true',
      inactive: root.dataset.hostInactive === 'true',
      orb: isOrbStyle(root.dataset.orb) ? root.dataset.orb : 'pearl' as const,
    };
  };
  const [look, setLook] = useState(read);
  useEffect(() => {
    const root = document.documentElement;
    const update = () => setLook(current => {
      const next = read();
      return next.light === current.light && next.reduce === current.reduce && next.constrained === current.constrained && next.hidden === current.hidden && next.inactive === current.inactive && next.orb === current.orb && next.colors.join() === current.colors.join() ? current : next;
    });
    update();
    const observer = new MutationObserver(update);
    observer.observe(root, { attributes: true, attributeFilter: ['data-theme', 'data-palette', 'data-reduce-motion', 'data-orb', 'data-resource-constrained', 'data-host-hidden', 'data-host-inactive'] });
    document.addEventListener('visibilitychange', update);
    return () => { observer.disconnect(); document.removeEventListener('visibilitychange', update); };
  }, []);
  return look;
}

type DialProps = { mode: 'dial'; minutes: number; onMinutesChange(minutes: number): void; disabled?: boolean; label: string; valueText: string };
type CountdownProps = { mode: 'countdown'; progress: number; segments?: readonly RingSegment[] | null };

/**
 * The FocusGuard orb: a decorative WebGL pearl wrapped by an accessible DOM ring. In dial mode the
 * ring is a slider for the session length; in countdown mode it shows the remaining fraction.
 */
export function FocusOrb(props: (DialProps | CountdownProps) & { children?: ReactNode; className?: string }) {
  const look = useDocumentLook();
  const systemReduce = useReducedMotion();
  const still = look.reduce || !!systemReduce || look.constrained || look.hidden || look.inactive;
  const [webgl, setWebgl] = useState(false);
  const pointer = useRef<OrbPointer>({ x: 0, y: 0, inside: false });
  const root = useRef<HTMLDivElement>(null);
  const tiltX = useMotionValue(0);
  const tiltY = useMotionValue(0);
  const haloX = useSpring(tiltX, { stiffness: 60, damping: 18 });
  const haloY = useSpring(tiltY, { stiffness: 60, damping: 18 });
  const dragging = useRef<{ last: number } | null>(null);
  const [isDragging, setDragging] = useState(false);
  const dial = props.mode === 'dial' ? props : null;
  const breathSeconds = props.mode === 'countdown' ? focusBreathSeconds : idleBreathSeconds;

  useEffect(() => { setWebgl(supportsWebGL()); }, []);

  useEffect(() => {
    const move = (event: globalThis.PointerEvent) => {
      const element = root.current;
      if (!element) return;
      const box = element.getBoundingClientRect();
      const reach = Math.max(box.width, 320) * 1.1;
      const x = Math.max(-1, Math.min(1, (event.clientX - (box.left + box.width / 2)) / reach));
      const y = Math.max(-1, Math.min(1, -(event.clientY - (box.top + box.height / 2)) / reach));
      pointer.current = { x, y, inside: true };
      if (!still) { tiltX.set(x * 18); tiltY.set(-y * 18); }
    };
    const leave = () => { pointer.current = { ...pointer.current, inside: false }; tiltX.set(0); tiltY.set(0); };
    window.addEventListener('pointermove', move, { passive: true });
    document.documentElement.addEventListener('pointerleave', leave);
    window.addEventListener('blur', leave);
    return () => {
      window.removeEventListener('pointermove', move);
      document.documentElement.removeEventListener('pointerleave', leave);
      window.removeEventListener('blur', leave);
    };
  }, [still, tiltX, tiltY]);

  const angleFromEvent = (event: PointerEvent<HTMLDivElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    return Math.atan2(event.clientX - (box.left + box.width / 2), -(event.clientY - (box.top + box.height / 2)));
  };
  const applyAngle = useCallback((angle: number) => {
    if (!dial || !dragging.current) return;
    let next = minutesForAngle(angle);
    // Do not wrap from the end of the dial back to its start (or vice versa) while dragging.
    const last = dragging.current.last;
    if (last >= dialMaximumMinutes - 20 && next <= 20) next = dialMaximumMinutes;
    else if (last <= 20 && next >= dialMaximumMinutes - 20) next = dialStep;
    dragging.current.last = next;
    if (next !== dial.minutes) dial.onMinutesChange(next);
  }, [dial]);

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!dial || dial.disabled) return;
    const steps: Record<string, number> = { ArrowUp: 1, ArrowRight: 1, ArrowDown: -1, ArrowLeft: -1, PageUp: dialStep, PageDown: -dialStep };
    let next: number | null = null;
    if (event.key in steps) next = dial.minutes + steps[event.key]!;
    else if (event.key === 'Home') next = minimumMinutes;
    else if (event.key === 'End') next = dialMaximumMinutes;
    if (next === null) return;
    event.preventDefault();
    dial.onMinutesChange(clampMinutes(next));
  };

  const fraction = props.mode === 'countdown'
    ? Math.max(0, Math.min(1, props.progress))
    : Math.min(1, Math.max(0, props.minutes) / dialMaximumMinutes);
  const handleAngle = fraction * Math.PI * 2;
  // A cycle session draws its phases; time already spent is dimmed and a dot marks now.
  const segments = props.mode === 'countdown' && props.segments?.length ? props.segments : null;
  const elapsed = 1 - fraction;
  const radius = 92;
  const handle = { x: 100 + radius * Math.sin(handleAngle), y: 100 - radius * Math.cos(handleAngle) };

  // Direct manipulation follows the pointer exactly; programmatic changes (chips, keys) settle with a spring.
  const settle = still ? { duration: 0 } : { type: 'spring' as const, duration: 0.4, bounce: 0 };
  const follow = isDragging || still ? { duration: 0 } : settle;
  return <div ref={root} className={`focus-orb ${props.className ?? ''}`} data-mode={props.mode} data-orb={look.orb} data-disabled={dial?.disabled ? 'true' : undefined} data-dragging={isDragging ? 'true' : undefined}>
    <motion.div className="orb-halo" aria-hidden="true" style={{ x: haloX, y: haloY }}
      animate={still ? { opacity: 0.7 } : { opacity: [0.55, 0.85, 0.55], scale: [0.96, 1.05, 0.96] }}
      transition={still ? { duration: 0.2 } : { duration: breathSeconds, repeat: Infinity, ease: 'easeInOut' }}/>
    <div className="orb-body" aria-hidden="true">
      {webgl && !look.constrained && !look.hidden ? <Suspense fallback={<div className="orb-fallback"/>}>
        <FocusOrbScene colors={look.colors} light={look.light} orbStyle={look.orb} pointer={pointer} breathSeconds={breathSeconds}
          energy={props.mode === 'countdown' ? 0.35 : isDragging ? 1.1 : 0.6} still={still} onFailure={() => setWebgl(false)}/>
      </Suspense> : <div className="orb-fallback"/>}
    </div>
    <svg className="orb-ring" viewBox="0 0 200 200" aria-hidden="true">
      <defs>
        <linearGradient id={`orb-ring-${props.mode}`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" className="orb-stop-a"/><stop offset="0.5" className="orb-stop-b"/><stop offset="1" className="orb-stop-c"/>
        </linearGradient>
      </defs>
      <circle className="orb-track" cx="100" cy="100" r={radius}/>
      {segments ? <g className="orb-segments">{segments.map((segment, index) => {
        const gap = 0.006;
        const trackLength = Math.max(0, segment.end - segment.start - gap);
        const liveStart = Math.max(segment.start, elapsed);
        const liveLength = Math.max(0, segment.end - liveStart - gap);
        return <g key={index} data-kind={segment.kind}>
          <circle className="orb-seg orb-seg-track" cx="100" cy="100" r={radius} pathLength={1} strokeDasharray={`${trackLength} 1`} strokeDashoffset={-segment.start} transform="rotate(-90 100 100)"/>
          {liveLength > 0 && <circle className="orb-seg orb-seg-live" cx="100" cy="100" r={radius} pathLength={1} strokeDasharray={`${liveLength} 1`} strokeDashoffset={-liveStart} transform="rotate(-90 100 100)"/>}
        </g>;
      })}<circle className="orb-now" cx={100 + radius * Math.sin(elapsed * Math.PI * 2)} cy={100 - radius * Math.cos(elapsed * Math.PI * 2)} r="4.5"/></g>
      : <motion.circle className="orb-progress" cx="100" cy="100" r={radius} stroke={`url(#orb-ring-${props.mode})`}
        transform="rotate(-90 100 100)" initial={false} animate={{ pathLength: Math.max(fraction, 0.0001) }}
        transition={dial ? follow : still ? { duration: 0 } : { type: 'spring', duration: 0.8, bounce: 0 }}/>}
      {dial && <motion.circle className="orb-handle" initial={false} animate={{ cx: handle.x, cy: handle.y, r: isDragging ? 8 : 5.5 }}
        transition={{ cx: follow, cy: follow, r: still ? { duration: 0 } : { type: 'spring', duration: 0.3, bounce: 0 } }}/>}
    </svg>
    {dial && <div className="orb-dial" role="slider" tabIndex={dial.disabled ? -1 : 0} aria-label={dial.label}
      aria-valuemin={minimumMinutes} aria-valuemax={maximumMinutes} aria-valuenow={dial.minutes} aria-valuetext={dial.valueText}
      aria-disabled={dial.disabled || undefined} onKeyDown={onKeyDown}
      onPointerDown={event => {
        if (dial.disabled || event.button !== 0) return;
        // Only the ring band is grabbable, so a stray click on the orb never changes the duration.
        const box = event.currentTarget.getBoundingClientRect();
        const distance = Math.hypot(event.clientX - (box.left + box.width / 2), event.clientY - (box.top + box.height / 2));
        if (distance < box.width * 0.36) return;
        event.currentTarget.setPointerCapture(event.pointerId);
        dragging.current = { last: dial.minutes };
        setDragging(true);
        applyAngle(angleFromEvent(event));
      }}
      onPointerMove={event => { if (dragging.current) applyAngle(angleFromEvent(event)); }}
      onPointerUp={event => { dragging.current = null; setDragging(false); if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId); }}
      onPointerCancel={() => { dragging.current = null; setDragging(false); }}/>}
    <div className="orb-center">{props.children}</div>
  </div>;
}
