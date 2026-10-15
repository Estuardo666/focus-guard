// Prototype admission rules only. No process, network or device enforcement.
export function admit(intent, context) {
  if (!Number.isFinite(context.nowMs) || !Number.isFinite(context.uncertaintyMs)
      || context.uncertaintyMs < 0 || !Number.isSafeInteger(context.maxRemoteDurationMs)
      || context.maxRemoteDurationMs < 1000 || context.localBusy !== false) {
    return context.localBusy === true ? 'SessionAlreadyActive' : 'InvalidContext';
  }
  if (!['Normal', 'Strict'].includes(intent.mode)) return 'InvalidMode';
  if (intent.generationId !== context.generationId) return 'GenerationMismatch';
  if (!context.activeMember) return 'DeviceRevoked';
  if (!context.targeted) return 'NotTargeted';
  if (context.alreadyProcessed) return 'AlreadyProcessed';
  const start = Date.parse(intent.startAtUtc), end = Date.parse(intent.endAtUtc);
  if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) return 'InvalidWindow';
  const cap = intent.mode === 'Strict' ? 14400000 : 86400000;
  if (end - start < 60000 || end - start > cap) return 'InvalidDuration';
  if (context.uncertaintyMs > 5000) return 'ClockUncertain';
  if (context.nowMs < start) return 'FutureStartUnsupported';
  if (end - context.nowMs < 1000) return 'Expired';
  if (end - context.nowMs > context.maxRemoteDurationMs) return 'ConsentDurationExceeded';
  if (context.localBusy) return 'SessionAlreadyActive';
  if (intent.mode === 'Strict') {
    if (!context.strictConsent) return 'ConsentRequired';
    if (!context.recoveryReady) return 'RecoveryNotReady';
    if (!context.ticket || !Number.isFinite(context.ticket.expiresMs) || context.nowMs >= context.ticket.expiresMs) return 'PreparationExpired';
    if (context.ticket.intentDigest !== context.intentDigest || context.ticket.consentRevision !== context.consentRevision
        || context.ticket.mappingRevision !== context.mappingRevision) return 'PreparationChanged';
  } else if (!context.normalConsent) return 'ConsentRequired';
  else if (context.nowMs - start > intent.normalLateJoinSeconds * 1000 && !context.localConfirmation) return 'LocalConfirmationRequired';
  return { outcome: 'Admitted', endAtUtc: intent.endAtUtc, remainingMs: end - context.nowMs };
}

export class PreparationSlot {
  #state = 'Preparing';
  #expiresMs;
  #endMs;
  constructor(expiresMs, endMs) { if (!Number.isFinite(expiresMs) || !Number.isFinite(endMs) || expiresMs <= 0 || endMs <= 0) throw new RangeError('Finite expiration required'); this.#expiresMs = Math.min(expiresMs, endMs); this.#endMs = endMs; }
  get expiresMs() { return this.#expiresMs; }
  get endMs() { return this.#endMs; }
  get state() { return this.#state; }
  ready(now) { if (!['Preparing', 'Ready'].includes(this.#state)) return this.#state; if (now >= this.expiresMs) return this.#state = 'PreparationExpired'; if (this.#state === 'Preparing') this.#state = 'Ready'; return this.#state; }
  commit(now) { if (this.#state === 'Committed') return 'Committed'; if (this.#state !== 'Ready') return 'Conflict'; if (now >= this.expiresMs) return this.#state = 'PreparationExpired'; return this.#state = 'Committed'; }
  cancel() { if (this.#state === 'Committed') return 'Conflict'; if (this.#state === 'Preparing' || this.#state === 'Ready') this.#state = 'Cancelled'; return this.#state; }
  expire(now) { if (this.#state === 'Committed' && now >= this.endMs) this.#state = 'Expired'; else if ((this.#state === 'Preparing' || this.#state === 'Ready') && now >= this.expiresMs) this.#state = 'PreparationExpired'; return this.#state; }
}
