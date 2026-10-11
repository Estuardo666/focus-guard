import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { SetupApp } from './SetupApp';
import { defaultSetupTransport } from './bridge';
import './setup.css';

const language = navigator.language.toLowerCase().startsWith('es') ? 'es' : 'en';
document.documentElement.lang = language;
document.documentElement.dataset.theme = matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
createRoot(document.getElementById('root')!).render(<StrictMode><SetupApp transport={defaultSetupTransport()} language={language}/></StrictMode>);
