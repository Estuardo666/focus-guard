import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import type { DashboardClient } from './native/dashboard';
import type { StrictReview } from './native/strict';
import type es from './locales/es.json';
import { countLabel } from './locales/count-label';
import { exitDialog } from './ZenControls';

type Copy = (key: keyof typeof es) => string;

const expiryWarningSeconds = 30;

function clock(seconds: number) {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

// Isolated so the per-second tick never re-renders the (long) rule lists.
function ReviewExpiry({ expiresUtc, t }: { expiresUtc: string; t: Copy }) {
  const remaining = () => Math.max(0, Math.ceil((Date.parse(expiresUtc) - Date.now()) / 1000));
  const [seconds, setSeconds] = useState(remaining);
  useEffect(() => {
    setSeconds(remaining());
    const timer = window.setInterval(() => setSeconds(remaining()), 1000);
    return () => window.clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [expiresUtc]);
  return <div className="strict-expiry">
    <p>{t('strictReviewExpiresIn').replace('{time}', clock(seconds))}</p>
    <p className="sr-only" role="status">{seconds <= expiryWarningSeconds ? t('strictReviewExpiringSoon') : ''}</p>
  </div>;
}

export function StrictReviewPanel({ dashboard, review, language, t, close, objective = '' }: {
  dashboard: DashboardClient; review: StrictReview; language: 'es' | 'en'; t: Copy; close(): void; objective?: string;
}) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const current = state.strictReview?.preparationId === review.preparationId ? state.strictReview : null;
  const dialog = useRef<HTMLDialogElement>(null);
  const closingForUnmount = useRef(false);
  const [step, setStep] = useState<1 | 2>(1);
  const [recoverySaved, setRecoverySaved] = useState(false);
  const [savingCode, setSavingCode] = useState(false);
  const [unsavedWorkConfirmed, setUnsavedWorkConfirmed] = useState(false);
  const [typedConfirmation, setTypedConfirmation] = useState('');
  const heading = useRef<HTMLHeadingElement>(null);
  const shownStep = useRef(step);
  const ends = new Date(review.endUtc);
  const endText = new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', {
    dateStyle: 'full', timeStyle: 'short',
  }).format(ends);
  const exactTarget = typedConfirmation.trim() === review.confirmationText;
  const busy = state.strictPhase === 'activating';

  useEffect(() => {
    const element = dialog.current;
    if (!element || element.open) return;
    element.showModal();
    return () => {
      closingForUnmount.current = true;
      if (element.open) element.close();
      closingForUnmount.current = false;
    };
  }, []);

  useEffect(() => {
    const remaining = Math.max(0, Date.parse(review.expiresUtc) - Date.now());
    const timer = window.setTimeout(() => dashboard.cancelStrictReview(), remaining);
    return () => window.clearTimeout(timer);
  }, [dashboard, review.preparationId, review.expiresUtc]);

  // The button that triggered a step change unmounts with it; without this, focus falls back to the dialog root.
  useEffect(() => {
    if (shownStep.current === step) return;
    shownStep.current = step;
    heading.current?.focus();
  }, [step]);

  useEffect(() => {
    setStep(1);
    setRecoverySaved(false);
    setSavingCode(false);
    setUnsavedWorkConfirmed(false);
    setTypedConfirmation('');
  }, [review.preparationId]);

  if (!current) return null;

  const pending = [
    ...(unsavedWorkConfirmed ? [] : [t('strictPendingAcknowledge')]),
    ...(exactTarget ? [] : [t('strictPendingTyping')]),
  ];

  return <dialog ref={dialog} className="strict-review-dialog" aria-labelledby="strict-review-title"
    onCancel={event => { event.preventDefault(); exitDialog(dialog.current, () => dialog.current?.close()); }} onClose={() => { if (!closingForUnmount.current) close(); }}>
    <section className="strict-review-panel">
      <header className="strict-review-heading">
        <div><p className="eyebrow">{t('strictReview')} · {step}/2</p>
          <h2 id="strict-review-title" ref={heading} tabIndex={-1}>{step === 1 ? t('strictSaveRecoveryCode') : t('strictLockUntil').replace('{time}', endText)}</h2>
          <p>{step === 1 ? t('strictRecoveryCodeDetail') : t('strictReviewDetail')}</p>
        </div>
        <button type="button" className="strict-review-close" disabled={busy} onClick={() => exitDialog(dialog.current, () => dialog.current?.close())}>{t('cancel')}</button>
      </header>

      {step === 1 ? <div className="strict-review-step">
        <div className="strict-recovery-code" role="group" aria-label={t('strictRecoveryCodeLabel')}>
          <code>{review.recoveryCode}</code>
          <p>{t('strictRecoveryCodeCopyHint')}</p>
        </div>
        <p className="notice">{t('strictRecoveryCodeWarning')}</p>
        <div className="strict-recovery-location"><span>{t('strictRecoveryHelper')}</span><code>{review.recoveryHelperPath}</code>
          {!review.recoveryHelperAvailable && <small>{t('strictRecoveryHelperMissing')}</small>}
          <small>{t('strictRecoveryManualUse').replace('{minutes}', String(review.recoveryDelaySeconds / 60))}</small>
        </div>
        <div className="review-actions">
          <button type="button" disabled={savingCode} onClick={() => exitDialog(dialog.current, () => dialog.current?.close())}>{t('cancel')}</button>
          <button type="button" disabled={savingCode} onClick={() => void (async () => {
            setSavingCode(true);
            const saved = await dashboard.saveStrictRecoveryCode();
            setSavingCode(false);
            if (saved) { setRecoverySaved(true); setStep(2); }
          })()}>{savingCode ? t('savingRecoveryCode') : t('strictDownloadCode')}</button>
          <button type="button" className="primary-action" disabled={savingCode} onClick={() => { setRecoverySaved(true); setStep(2); }}>{t('strictSavedIt')}</button>
        </div>
        {state.strictError && <p className="notice" role="alert">{t(state.strictError === 'RecoverySaveCancelled' ? 'strictRecoverySaveCancelled' : 'strictRecoverySaveFailed')}</p>}
        <ReviewExpiry expiresUtc={review.expiresUtc} t={t}/>
      </div> :<div className="strict-review-step">
        <div className="strict-review-facts">
          <article><span>{t('profile')}</span><strong>{review.profileName}</strong></article>
          <article><span>{t('strictMode')}</span><strong>{t(review.mode === 'FocusCycle' ? 'cycleOption' : 'timerOption')}</strong></article>
          <article><span>{t('strictEnd')}</span><strong>{endText}</strong></article>
          <article><span>{t('strictRecoveryDelay')}</span><strong>{review.recoveryDelaySeconds / 60} {t('minutesShort')}</strong></article>
          <article><span>{t('blocking')}</span><strong>{review.enforcementEnabled ? t('enabled') : t('notEnabled')}</strong></article>
        </div>
        {objective.trim() && <div className="strict-objective-review"><strong>{t('sessionObjectiveLabel')}</strong><p>{objective.trim()}</p><small>{t('sessionObjectiveLocal')}</small></div>}

        {review.enforcementEnabled && <>
          <div className="strict-rule-summary"><strong>{countLabel(review.blockedApplicationCount, t, 'singleBlockedApplication', 'multipleBlockedApplications')} · {countLabel(review.blockedWebsiteCount, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')}</strong>
            <div className="strict-rule-columns">
              <section><h3>{t('blockedApplications')}</h3>{review.blockedApplications.length ? <ul>{review.blockedApplications.map((item, index) => <li key={`${index}:${item}`}>{item}</li>)}</ul> : <p>{t('none')}</p>}</section>
              <section><h3>{t('blockedWebsites')}</h3>{review.blockedWebsites.length ? <ul>{review.blockedWebsites.map((item, index) => <li key={`${index}:${item}`}>{item}</li>)}</ul> : <p>{t('none')}</p>}</section>
            </div>
            {review.rulesTruncated && <p className="start-note">{t('strictRulesTruncated')}</p>}
          </div>
          {review.blockedWebsiteCount > 0 && <p className={review.extensionReady ? 'strict-health-ready' : 'notice'}>{review.extensionReady ? t('extensionReady') : t('browserWarning')}</p>}
        </>}

        {!review.enforcementEnabled && <p className="start-note">{t('strictTimerOnlyWarning')}</p>}
        {review.mode === 'FocusCycle' && <>
          <section className="strict-cycle-summary"><h3>{review.presetName ?? t('customConfiguration')}</h3>
            {review.configuration && <p>{review.configuration.focusSeconds / 60} {t('focusMinutes')} · {review.configuration.shortBreakSeconds / 60} {t('shortBreakMinutes')} · {review.configuration.longBreakSeconds / 60} {t('longBreakMinutes')} · {review.configuration.terminationMode === 'CycleCount' ? `${review.configuration.cycleCount} ${t('focusCycles')}` : `${review.configuration.totalDurationSeconds! / 60} ${t('totalMinutes')}`}</p>}
            <ol className="cycle-schedule" tabIndex={0} aria-label={t('cycleSchedule')}>{review.phases.map(phase => <li key={phase.index}><span>{phase.focusCycleNumber} · {t(phase.kind)}</span><span>{new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { timeStyle: 'short' }).format(new Date(phase.startUtc))} → {new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { timeStyle: 'short' }).format(new Date(phase.endUtc))}</span></li>)}</ol>
            {review.partialFinalPhase && <p className="notice">{t('partialFinalPhase')}</p>}
            {review.enforcementEnabled && (review.breakApplicationRuleCount > 0 || review.breakWebsiteRuleCount > 0) && <div className="strict-break-rules"><strong>{t('breakRuleSummary')}: {countLabel(review.breakApplicationRuleCount, t, 'singleBlockedApplication', 'multipleBlockedApplications')} · {countLabel(review.breakWebsiteRuleCount, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')}</strong><p className="start-note">{t('strictBreakExceptionMeaning')}</p><div className="strict-rule-columns"><section><h3>{t('breakExceptionApps')}</h3>{review.breakApplications.length ? <ul>{review.breakApplications.map((item, index) => <li key={`${index}:${item}`}>{item}</li>)}</ul> : <p>{t('none')}</p>}</section><section><h3>{t('breakExceptionSites')}</h3>{review.breakWebsites.length ? <ul>{review.breakWebsites.map((item, index) => <li key={`${index}:${item}`}>{item}</li>)}</ul> : <p>{t('none')}</p>}</section></div></div>}
          </section>
        </>}

        <div className="strict-limits-note"><strong>{t('strictNotUnbreakableTitle')}</strong><p>{t('strictNotUnbreakableDetail')}</p></div>
        <p className="notice">{t('strictUnsavedWorkWarning')}</p>
        <label className="strict-unsaved-confirm"><input type="checkbox" checked={unsavedWorkConfirmed} onChange={event => setUnsavedWorkConfirmed(event.target.checked)}/><span>{t('strictUnsavedWorkAcknowledgment')}</span></label>
        <label className="strict-confirm-label">{t('strictTypeProfileName')}<input type="text" value={typedConfirmation} onChange={event => setTypedConfirmation(event.target.value)} autoComplete="off" spellCheck={false} /></label>
        {state.strictError && <p className="notice" role="alert">{t(state.strictError === 'OutcomeUnknown' ? 'strictActivationUnknown' : 'strictActivationError')}</p>}
        <footer className="strict-review-footer">
        <div id="strict-confirm-requirements" className="strict-requirements" data-ready={pending.length === 0} role="status">
          {pending.length ? <ul>{pending.map(item => <li key={item}>{item}</li>)}</ul> : <p>{t('strictReadyToStart')}</p>}
        </div>
        <ReviewExpiry expiresUtc={review.expiresUtc} t={t}/>
        <div className="review-actions">
          <button type="button" disabled={busy} onClick={() => { setStep(1); setTypedConfirmation(''); setUnsavedWorkConfirmed(false); }}>{t('strictBackToCode')}</button>
          <button type="button" disabled={busy} onClick={() => exitDialog(dialog.current, () => dialog.current?.close())}>{t('cancel')}</button>
          <button type="button" className="danger-action" aria-describedby="strict-confirm-requirements" disabled={busy || !recoverySaved || !unsavedWorkConfirmed || !exactTarget}
            onClick={() => void dashboard.confirmStrict(typedConfirmation, recoverySaved, unsavedWorkConfirmed, objective)}>{busy ? t('strictStarting') : t('strictConfirmButton').replace('{time}', new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { timeStyle: 'short' }).format(ends))}</button>
        </div>
        </footer>
        {state.strictPhase === 'unknown' && <p className="start-note">{t('strictActivationNoRetry')}</p>}
      </div>}
    </section>
  </dialog>;
}

export function RecoveryAccess({ dashboard, t }: { dashboard: DashboardClient; t: Copy }) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const requested = useRef(false);
  useEffect(() => { if (!requested.current) { requested.current = true; void dashboard.loadRecoveryInfo(); } }, [dashboard]);
  return <article className="recovery-access" aria-labelledby="recovery-access-title">
    <div><h2 id="recovery-access-title">{t('recoveryAccessTitle')}</h2><p>{t('recoveryAccessDetail')}</p></div>
    {state.recoveryInfoLoading && <p role="status">{t('recoveryInfoLoading')}</p>}
    {state.recoveryInfoError && <p className="notice" role="alert">{t('recoveryInfoError')} <button type="button" disabled={state.recoveryInfoLoading} onClick={() => void dashboard.loadRecoveryInfo()}>{t('refresh')}</button></p>}
    {state.recoveryInfo && <><code>{state.recoveryInfo.executablePath}</code>
      {!state.recoveryInfo.available && <p className="notice">{t('recoveryHelperMissing')}</p>}
      <p className="start-note">{t('recoveryAccessSteps').replace('{minutes}', String(state.recoveryInfo.recoveryDelaySeconds / 60))}</p></>}
  </article>;
}
