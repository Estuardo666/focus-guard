import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StrictReviewPanel } from './StrictReviewPanel';
import type { DashboardClient, DashboardSnapshot } from './native/dashboard';
import type { StrictReview } from './native/strict';
import es from './locales/es.json';

const review: StrictReview = {
  preparationId: '33333333333343338333333333333333', profileId: '22222222-2222-4222-8222-222222222222',
  profileName: 'Enfoque', mode: 'SimpleTimer', strict: true, durationSeconds: 3600, enforcementEnabled: true,
  applicationRuleCount: 1, websiteRuleCount: 1, blockedApplicationCount: 1, blockedWebsiteCount: 1,
  blockedApplications: ['editor.exe'], blockedWebsites: ['social.example'], breakApplicationRuleCount: 0,
  breakWebsiteRuleCount: 0, breakApplications: [], breakWebsites: [], rulesTruncated: false,
  presetName: null, presetId: null, configuration: null,
  startUtc: new Date(Date.now() + 10_000).toISOString(),
  endUtc: new Date(Date.now() + 3_610_000).toISOString(),
  expiresUtc: new Date(Date.now() + 130_000).toISOString(), phases: [],
  partialFinalPhase: false, extensionReady: true, recoveryDelaySeconds: 900,
  recoveryCode: 'ABCDE-12345-FEDCB-67890-ABCDF-12345-FEDCB-67890',
  confirmationText: 'Enfoque', recoveryHelperPath: 'C:\\Program Files\\FocusGuard\\FocusGuard.Recovery.exe',
  recoveryHelperAvailable: true,
};

function makeDashboard() {
  const listeners = new Set<() => void>();
  const state = Object.freeze({ strictReview: review, strictPhase: 'review', strictError: null }) as unknown as DashboardSnapshot;
  return {
    subscribe(listener: () => void) { listeners.add(listener); return () => listeners.delete(listener); },
    getSnapshot: () => state,
    cancelStrictReview: vi.fn(),
    saveStrictRecoveryCode: vi.fn(async () => true),
    confirmStrict: vi.fn(async () => null),
  } as unknown as DashboardClient;
}

describe('StrictReviewPanel', () => {
  let root: Root;
  let container: HTMLDivElement;
  const dashboard = makeDashboard();
  beforeEach(() => {
    Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
    HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
    HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.clearAllMocks(); });

  it('requires a saved code, unsaved-work acknowledgment and exact profile name before activation', async () => {
    await act(async () => root.render(<StrictReviewPanel dashboard={dashboard} review={review} objective="Finish draft" language="es" t={key => es[key]} close={vi.fn()}/>));
    expect(container.querySelector('dialog')?.textContent).toContain(review.recoveryCode);
    const buttons = () => Array.from(container.querySelectorAll<HTMLButtonElement>('button'));
    const next = buttons().find(button => button.textContent === es.strictSavedIt)!;
    expect(buttons().some(button => button.textContent?.includes(es.strictConfirmButton.split('{')[0] ?? ''))).toBe(false);
    await act(async () => next.click());
    expect(container.querySelector('dialog')?.textContent).toContain('Finish draft');
    const start = buttons().find(button => button.textContent?.startsWith('Iniciar modo estricto'))!;
    expect(start.disabled).toBe(true);
    const confirmation = container.querySelector<HTMLInputElement>('input[type="text"]')!;
    await act(async () => { const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!; setter.call(confirmation, 'Enfoque'); confirmation.dispatchEvent(new Event('input', { bubbles: true })); });
    expect(start.disabled).toBe(true);
    const acknowledgment = container.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    await act(async () => { acknowledgment.click(); });
    expect(start.disabled).toBe(false);
    await act(async () => start.click());
    expect(dashboard.confirmStrict).toHaveBeenCalledWith('Enfoque', true, true, 'Finish draft');
  });

  it('never shows an unreplaced placeholder and states the real recovery delay in both steps', async () => {
    await act(async () => root.render(<StrictReviewPanel dashboard={dashboard} review={review} objective="Finish draft" language="es" t={key => es[key]} close={vi.fn()}/>));
    const text = () => container.querySelector('dialog')!.textContent ?? '';
    expect(text()).not.toMatch(/\{\w+\}/);
    expect(text()).toContain('espera mínima de 15 minutos');
    await act(async () => Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent === es.strictSavedIt)!.click());
    expect(text()).not.toMatch(/\{\w+\}/);
  });

  it('moves focus to the new step heading and explains what still blocks activation', async () => {
    await act(async () => root.render(<StrictReviewPanel dashboard={dashboard} review={review} language="es" t={key => es[key]} close={vi.fn()}/>));
    const buttons = () => Array.from(container.querySelectorAll<HTMLButtonElement>('button'));
    await act(async () => buttons().find(button => button.textContent === es.strictSavedIt)!.click());
    expect(document.activeElement).toBe(container.querySelector('#strict-review-title'));

    const start = buttons().find(button => button.textContent?.startsWith('Iniciar modo estricto'))!;
    const requirements = container.querySelector('#strict-confirm-requirements')!;
    expect(start.getAttribute('aria-describedby')).toBe(requirements.id);
    expect(Array.from(requirements.querySelectorAll('li')).map(item => item.textContent)).toEqual([es.strictPendingAcknowledge, es.strictPendingTyping]);

    await act(async () => container.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click());
    expect(Array.from(requirements.querySelectorAll('li')).map(item => item.textContent)).toEqual([es.strictPendingTyping]);

    const confirmation = container.querySelector<HTMLInputElement>('input[type="text"]')!;
    await act(async () => { const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!; setter.call(confirmation, 'Enfoque'); confirmation.dispatchEvent(new Event('input', { bubbles: true })); });
    expect(requirements.textContent).toBe(es.strictReadyToStart);
    expect(requirements.getAttribute('data-ready')).toBe('true');

    await act(async () => buttons().find(button => button.textContent === es.strictBackToCode)!.click());
    expect(document.activeElement).toBe(container.querySelector('#strict-review-title'));
  });

  it('shows when the review expires and announces the last 30 seconds once', async () => {
    vi.useFakeTimers();
    try {
      const expiring = { ...review, expiresUtc: new Date(Date.now() + 65_000).toISOString() };
      await act(async () => root.render(<StrictReviewPanel dashboard={dashboard} review={expiring} language="es" t={key => es[key]} close={vi.fn()}/>));
      const expiry = container.querySelector('.strict-expiry')!;
      expect(expiry.textContent).toContain(es.strictReviewExpiresIn.replace('{time}', '1:05'));
      expect(expiry.querySelector('[role="status"]')!.textContent).toBe('');
      await act(async () => { vi.advanceTimersByTime(36_000); });
      expect(expiry.textContent).toContain(es.strictReviewExpiresIn.replace('{time}', '0:29'));
      expect(expiry.querySelector('[role="status"]')!.textContent).toBe(es.strictReviewExpiringSoon);
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not claim break exceptions stay blocked when blocking is disabled', async () => {
    const cycle = { ...review, mode: 'FocusCycle' as const, enforcementEnabled: false, breakApplicationRuleCount: 1, breakApplications: ['chat.exe'] };
    const withBlocking = { ...cycle, enforcementEnabled: true };
    const render = async (value: StrictReview) => act(async () => root.render(<StrictReviewPanel dashboard={makeDashboard()} review={value} language="es" t={key => es[key]} close={vi.fn()}/>));
    const toStepTwo = async () => act(async () => Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent === es.strictSavedIt)!.click());
    await render(cycle); await toStepTwo();
    expect(container.querySelector('.strict-break-rules')).toBeNull();
    expect(container.textContent).not.toContain(es.strictBreakExceptionMeaning);
    await act(async () => root.unmount()); root = createRoot(container);
    await render(withBlocking); await toStepTwo();
    expect(container.querySelector('.strict-break-rules')).not.toBeNull();
  });

  it('saves the code through the Host before advancing when the user chooses a file', async () => {
    await act(async () => root.render(<StrictReviewPanel dashboard={dashboard} review={review} language="es" t={key => es[key]} close={vi.fn()}/>));
    const save = Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent === es.strictDownloadCode)!;
    await act(async () => save.click());
    expect(dashboard.saveStrictRecoveryCode).toHaveBeenCalledTimes(1);
    expect(container.querySelector('input[type="text"]')).not.toBeNull();
    expect(container.querySelector('dialog')?.textContent).toContain(es.strictUnsavedWorkWarning);
  });
});
