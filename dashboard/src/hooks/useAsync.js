/**
 * Loading state for a request whose result belongs to the current view.
 *
 * The `alive` guard matters here: switching containers quickly enough would otherwise let a slow
 * first response overwrite a fast second one.
 */

import { useCallback, useEffect, useRef, useState } from 'react';

export default function useAsync(loader, dependencies = [], { immediate = true } = {}) {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(immediate);
  const generation = useRef(0);

  const run = useCallback(async (...args) => {
    generation.current += 1;
    const current = generation.current;

    setLoading(true);
    setError(null);
    try {
      const result = await loader(...args);
      if (generation.current === current) setData(result);
      return result;
    } catch (caught) {
      if (generation.current === current) setError(caught);
      throw caught;
    } finally {
      if (generation.current === current) setLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, dependencies);

  useEffect(() => {
    if (!immediate) return;
    run().catch(() => {
      // The error is already in state; an unhandled rejection here would only add noise.
    });
  }, [run, immediate]);

  return { data, error, loading, run, setData };
}
