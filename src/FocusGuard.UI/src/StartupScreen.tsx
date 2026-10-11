import es from './locales/es.json';
import en from './locales/en.json';

export function StartupScreen({ language = document.documentElement.lang === 'en' ? 'en' : 'es' }: { language?: 'es' | 'en' }) {
  return <div className="startup-screen" role="status" aria-live="polite">
    <span className="startup-pearl" aria-hidden="true"/>
    <strong>FocusGuard</strong>
    <span>{(language === 'es' ? es : en).startupLoading}</span>
  </div>;
}
