import { Component, type ReactNode } from 'react';
import es from './locales/es.json';
import en from './locales/en.json';

export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() {
    if (!this.state.failed) return this.props.children;
    const copy = document.documentElement.lang === 'en' ? en : es;
    return <main className="main"><section className="help-list" role="alert"><article><h1>{copy.renderError}</h1><p>{copy.renderErrorDetail}</p><button type="button" className="icon-button" onClick={() => location.reload()}>{copy.reload}</button></article></section></main>;
  }
}
