import { createContext, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { listWorkspaces } from '../api/workspaces';
import type { Workspace } from '../api/types';
import { ApiError } from '../api/client';

interface WorkspaceContextValue {
  status: 'loading' | 'ready' | 'error';
  message?: string;
  workspaces: Workspace[];
  current?: Workspace;
  setCurrentId: (id: string) => void;
}

const WorkspaceContext = createContext<WorkspaceContextValue | undefined>(undefined);

export function WorkspaceProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [message, setMessage] = useState<string>();
  const [workspaces, setWorkspaces] = useState<Workspace[]>([]);
  const [currentId, setCurrentId] = useState<string>();

  useEffect(() => {
    const controller = new AbortController();
    listWorkspaces()
      .then((list) => {
        if (controller.signal.aborted) return;
        setWorkspaces(list);
        setCurrentId((prev) => prev ?? list[0]?.id);
        setStatus('ready');
      })
      .catch((err) => {
        if (controller.signal.aborted) return;
        setMessage(err instanceof ApiError ? err.message : 'Unexpected error');
        setStatus('error');
      });
    return () => controller.abort();
  }, []);

  const value = useMemo<WorkspaceContextValue>(
    () => ({
      status,
      message,
      workspaces,
      current: workspaces.find((w) => w.id === currentId) ?? workspaces[0],
      setCurrentId,
    }),
    [status, message, workspaces, currentId],
  );

  return <WorkspaceContext.Provider value={value}>{children}</WorkspaceContext.Provider>;
}

export function useWorkspace(): WorkspaceContextValue {
  const ctx = useContext(WorkspaceContext);
  if (!ctx) throw new Error('useWorkspace must be used within a WorkspaceProvider');
  return ctx;
}
