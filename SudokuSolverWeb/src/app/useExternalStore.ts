import { useSyncExternalStore } from "react";

export interface ExternalStore<TSnapshot> {
  getSnapshot(): TSnapshot;
  subscribe(listener: () => void): () => void;
}

export function useExternalStore<TSnapshot>(
  store: ExternalStore<TSnapshot>,
): TSnapshot {
  return useSyncExternalStore(
    store.subscribe,
    store.getSnapshot,
    store.getSnapshot,
  );
}
