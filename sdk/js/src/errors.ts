import type { ApiError } from './types.js';

/**
 * Thrown when a PepperX server returns an error response.
 *
 * Carries the server's typed classification and status code so callers can branch on values rather
 * than parsing message text.
 */
export class PepperXError extends Error {
  /** Machine-readable error classification reported by the server. */
  readonly errorType: ApiError;

  /** HTTP status code reported by the server. */
  readonly statusCode: number;

  /** Raw response body, when available. */
  readonly body: string | null;

  /**
   * @param errorType Error classification.
   * @param statusCode HTTP status code.
   * @param message Human-readable message.
   * @param body Raw response body, when available.
   */
  constructor(errorType: ApiError, statusCode: number, message: string, body: string | null = null) {
    super(message);
    this.name = 'PepperXError';
    this.errorType = errorType;
    this.statusCode = statusCode;
    this.body = body;
  }
}
