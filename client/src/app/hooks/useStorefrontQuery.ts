import { useEffect, useState } from 'react';
import { ApiErrorData, toApiErrorData } from '../api/contracts';

type QueryResult<T> = { status: 'loading' } | { status: 'ready'; data: T } | { status: 'error'; error: ApiErrorData };

export default function useStorefrontQuery<T>(load: () => Promise<T>) {
  const [attempt, setAttempt] = useState(0);
  const [settled, setSettled] = useState<{ load: typeof load; attempt: number; result: QueryResult<T> }>();

  useEffect(() => {
    let active = true;
    load().then(
      data => { if (active) setSettled({ load, attempt, result: { status: 'ready', data } }); },
      error => { if (active) setSettled({ load, attempt, result: { status: 'error', error: toApiErrorData(error) } }); },
    );
    return () => { active = false; };
  }, [load, attempt]);

  const result: QueryResult<T> = settled?.load === load && settled.attempt === attempt ? settled.result : { status: 'loading' };
  return { result, retry: () => setAttempt(value => value + 1) };
}
