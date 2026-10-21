// TanStack Form hands back Standard Schema issues (objects with a message) for Zod validators and plain strings for
// function validators; a field shows the first one, worded by the schema.
export function firstErrorMessage(errors: ReadonlyArray<unknown>): string | undefined {
  for (const error of errors) {
    if (typeof error === 'string') return error;
    if (typeof error === 'object' && error !== null && 'message' in error && typeof error.message === 'string') {
      return error.message;
    }
  }
  return undefined;
}

type FieldMeta = { readonly errors: ReadonlyArray<unknown>; readonly isBlurred: boolean };

// Errors wait until the person leaves the field or tries to submit, so typing is never interrupted by red text.
export function visibleError(meta: FieldMeta, submissionAttempts: number): string | undefined {
  if (!meta.isBlurred && submissionAttempts === 0) return undefined;
  return firstErrorMessage(meta.errors);
}
