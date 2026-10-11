import { useSyncExternalStore } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { ErrorBoundary } from './ErrorBoundary';
import { SurfaceApp } from './Surfaces';
import { createNativeStatusClient } from './native/client';
import { createDashboardClient } from './native/dashboard';
import { createProfilesClient } from './native/profiles';
import { createApplicationsClient } from './native/applications';
import { createWindowControlsClient } from './native/window-controls';
import { createShellClient } from './native/shell';
import './design-system/tokens.css';
import './design-system/compact-layout.css';
import './design-system/accessibility-controls.css';
import './design-system/zen.css';

const root = document.getElementById('root');
if (!root) throw new Error('Missing application root');
// Subscribe before React's deferred effects: NavigationCompleted can send initialization immediately.
const shell = createShellClient();
shell.start();
const client = createNativeStatusClient();
client.start();
const dashboard = createDashboardClient();
dashboard.start();
const profiles = createProfilesClient();
profiles.start();
const applications = createApplicationsClient();
applications.start();
const windowControls = createWindowControlsClient();
windowControls.start();

/** One page, two roles: the main window, or a popup the host announced (tray panel, notice, summary, block screen). */
function Root() {
  const state = useSyncExternalStore(shell.subscribe, shell.getSnapshot);
  if (state.mode === 'surface' && state.surface) return <SurfaceApp surface={state.surface} shell={shell} client={client} dashboard={dashboard}/>;
  if (state.mode === 'unknown') return <div className="boot" aria-hidden="true"/>;
  return <App client={client} dashboard={dashboard} profiles={profiles} applications={applications} windowControls={windowControls} shell={shell}/>;
}

createRoot(root).render(<ErrorBoundary><Root/></ErrorBoundary>);