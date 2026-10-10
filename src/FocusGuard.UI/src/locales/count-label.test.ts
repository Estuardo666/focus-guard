import { countLabel } from './count-label';
import es from './es.json';

it('uses singular Spanish labels for one rule and plural labels otherwise', () => {
  const t = (key: keyof typeof es) => es[key];
  expect(countLabel(1, t, 'singleApplicationRule', 'multipleApplicationRules')).toBe('1 regla de aplicación');
  expect(countLabel(2, t, 'singleApplicationRule', 'multipleApplicationRules')).toBe('2 reglas de aplicación');
  expect(countLabel(1, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')).toBe('1 sitio con regla de bloqueo');
});
