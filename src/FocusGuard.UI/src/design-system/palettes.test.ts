import { isPalette, loadPalette, palettes, savePalette } from './palettes';

afterEach(() => { localStorage.clear(); vi.restoreAllMocks(); });

it('defaults to aurora and only accepts known palettes', () => {
  expect(loadPalette()).toBe('aurora');
  localStorage.setItem('focusguard.palette', 'neon');
  expect(loadPalette()).toBe('aurora');
  expect(palettes.every(isPalette)).toBe(true);
  savePalette('ocean');
  expect(loadPalette()).toBe('ocean');
});

it('keeps working when browser storage is unavailable', () => {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });
  expect(() => savePalette('ember')).not.toThrow();
  expect(loadPalette()).toBe('aurora');
});

it('remembers the orb style and the review step locally, with safe defaults', async () => {
  const { loadOrbStyle, saveOrbStyle, loadReviewBeforeStart, saveReviewBeforeStart } = await import('./palettes');
  localStorage.clear();
  expect(loadOrbStyle()).toBe('pearl');
  expect(loadReviewBeforeStart()).toBe(true);
  saveOrbStyle('cloud'); saveReviewBeforeStart(false);
  expect(loadOrbStyle()).toBe('cloud');
  expect(loadReviewBeforeStart()).toBe(false);
  localStorage.setItem('focusguard.orb', 'disco');
  expect(loadOrbStyle()).toBe('pearl');
  localStorage.clear();
});
