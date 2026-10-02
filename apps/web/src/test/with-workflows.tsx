import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";

import type { ProcessingLibrary } from "../lib/processing/libraries-api";
import { processingKeys } from "../lib/processing/query-keys";

/** A workflow as the libraries query returns it, with only what a colour is taken from. */
export function workflow(
  id: number,
  name: string,
  displayOrder: number,
): ProcessingLibrary {
  return { id, name, display_order: displayOrder } as ProcessingLibrary;
}

/** The two workflows most tests have files in, in Settings order. */
export const MOVIES_AND_TV = [workflow(1, "Movies", 0), workflow(2, "TV", 1)];

/** Gives what is under it the workflows already loaded, as they are on any page, so nothing is fetched. */
export function WithWorkflows({
  workflows = MOVIES_AND_TV,
  children,
}: {
  workflows?: ProcessingLibrary[];
  children: ReactNode;
}) {
  const [client] = useState(() => {
    const created = new QueryClient({
      defaultOptions: { queries: { staleTime: Infinity, retry: false } },
    });
    created.setQueryData(processingKeys.libraries, workflows);
    return created;
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
