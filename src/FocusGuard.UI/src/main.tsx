import { createRoot } from 'react-dom/client';
import { App } from './App';
import { ErrorBoundary } from './ErrorBoundary';
import { createNativeStatusClient } from './native/client';
import { createDashboardClient } from './native/dashboard';
import { createProfilesClient } from './native/profiles';
import { createApplicationsClient } from './native/applications';
import { createWindowControlsClient } from './native/window-controls';
import './design-system/tokens.css';
import './design-system/compact-layout.css';
import './design-system/accessibility-controls.css';

const root = document.getElementById('root');
if (!root) throw new Error('Missing application root');
const client = createNativeStatusClient();
// Subscribe before React's deferred effects: NavigationCompleted can send initialization immediately.
client.start();
const dashboard = createDashboardClient();
dashboard.start();
const profiles = createProfilesClient();
profiles.start();
const applications = createApplicationsClient();
applications.start();
const windowControls = createWindowControlsClient();
windowControls.start();
createRoot(root).render(<ErrorBoundary><App client={client} dashboard={dashboard} profiles={profiles} applications={applications} windowControls={windowControls}/></ErrorBoundary>);
