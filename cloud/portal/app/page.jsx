'use client';
import { useEffect, useState } from 'react';

export default function AccountPage() {
  const [account, setAccount] = useState(null);
  const [status, setStatus] = useState('loading');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    fetch('/api/auth/me', { cache: 'no-store', signal: controller.signal }).then(async response => {
      if (!response.ok) { setStatus('unavailable'); return; }
      const value = await response.json(); setAccount(value); setStatus(value.signedIn ? 'signedIn' : 'anonymous');
    }).catch(error => { if (error.name !== 'AbortError') setStatus('unavailable'); });
    return () => controller.abort();
  }, []);
  async function logout() {
    setBusy(true);
    try {
      const response = await fetch('/api/auth/logout', { method: 'POST', headers: { 'X-FG-CSRF': account.csrf } });
      if (!response.ok) { setStatus('error'); return; }
      const result = await response.json(); setAccount(null); setStatus(result.remoteRevoked ? 'anonymous' : 'localLogout');
    } catch { setStatus('error'); }
    finally { setBusy(false); }
  }
  const signedIn = account?.signedIn;
  const initials = (account?.displayName || account?.email || 'FG').split(/[\s@.]+/).filter(Boolean).slice(0, 2).map(part => part[0].toUpperCase()).join('');
  return <main className="shell"><div className="atmosphere" aria-hidden="true" />
    <header className="brand"><span className="brand-mark" aria-hidden="true" /><span>FocusGuard</span><span className="stage">Pruebas</span></header>
    <div className="account-layout">
      <section className="intro" aria-labelledby="account-title">
        <div className="focus-orb" aria-hidden="true"><span className="orb-halo" /><span className="orb-body" /><span className="orb-shine" /></div>
        <p className="eyebrow">Un espacio para enfocarte</p>
        <h1 id="account-title">Tu cuenta.<br /><span>Tu ritmo.</span></h1>
        <p className="lead">Conecta tu cuenta para preparar tus dispositivos. Tus sesiones locales siguen funcionando por su cuenta.</p>
        <ul className="trust" aria-label="Garantías">
          <li><span aria-hidden="true">◆</span>Cifrado de extremo a extremo</li>
          <li><span aria-hidden="true">◆</span>Sin historial de navegación</li>
          <li><span aria-hidden="true">◆</span>Tus reglas siguen en tu equipo</li>
        </ul>
      </section>
      <section className="panel" aria-label="Cuenta de FocusGuard">
        <div className="panel-heading">
          {signedIn && <span className="avatar" aria-hidden="true">{initials}</span>}
          <div className="panel-title"><h2>{signedIn ? 'Hola, ' + (account.displayName || 'bienvenido') : 'Conecta tu cuenta'}</h2>
            <p className="muted">{signedIn ? account.email : 'El inicio de sesión se abre en nuestra página segura de identidad.'}</p></div>
          <span className={'status-pill' + (signedIn ? ' is-on' : '')}><span className="status-dot" aria-hidden="true" />{signedIn ? 'Conectada' : 'Sin conectar'}</span>
        </div>
        {status === 'loading' && <p className="muted" role="status">Comprobando tu cuenta…</p>}
        {status === 'unavailable' && <p className="notice" role="status">La conexión de cuenta aún no está disponible. Puedes seguir usando FocusGuard en tu equipo.</p>}
        {status === 'error' && <p className="notice" role="alert">No pudimos completar la desconexión. Vuelve a intentarlo.</p>}
        {status === 'localLogout' && <p className="notice" role="status">Este portal está desconectado. No pudimos confirmar el cierre de la sesión en identidad.</p>}
        {!signedIn && <a className={'primary ' + (status === 'loading' || status === 'unavailable' ? 'disabled' : '')} aria-disabled={status === 'loading' || status === 'unavailable'} href={status === 'loading' || status === 'unavailable' ? undefined : '/api/auth/login'}>Iniciar sesión o crear cuenta <span aria-hidden="true">↗</span></a>}
        {signedIn && <>
          <dl className="facts"><div className={account.emailVerified ? 'ok' : 'pending'}><dt>Correo</dt><dd>{account.emailVerified ? 'Verificado' : 'Pendiente de verificación'}</dd></div><div className="pending"><dt>Dispositivos</dt><dd>Se vinculan desde la app</dd></div></dl>
          <a className="primary" href="/api/auth/security">Perfil y seguridad <span aria-hidden="true">↗</span></a>
          <button className="secondary danger" onClick={logout} disabled={busy}>{busy ? 'Desconectando…' : 'Cerrar sesión'}</button>
        </>}
        <div className="privacy"><h3>Lo que permanece contigo</h3><p>La cuenta no contiene tu historial de navegación ni tus claves de recuperación. Cerrar sesión aquí no cancela un enfoque activo en tus dispositivos.</p></div>
      </section>
    </div>
    <footer>Las órdenes entre dispositivos todavía no están disponibles en este portal.</footer>
  </main>;
}
