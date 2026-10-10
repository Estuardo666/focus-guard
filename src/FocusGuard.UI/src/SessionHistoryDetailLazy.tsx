import { lazy } from 'react';

export const SessionHistoryDetailLazy = lazy(() =>
  import('./SessionHistoryDetail').then(module => ({ default: module.SessionHistoryDetail })));
