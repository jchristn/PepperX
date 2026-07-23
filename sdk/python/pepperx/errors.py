"""Exceptions raised by the PepperX client."""

from __future__ import annotations

from typing import Optional

from .models import ApiError


class PepperXError(Exception):
    """Raised when a PepperX server returns an error response.

    Carries the server's typed classification and status code so callers can branch on values
    rather than parsing message text.
    """

    def __init__(
        self,
        error_type: ApiError,
        status_code: int,
        message: str,
        body: Optional[str] = None,
    ) -> None:
        super().__init__(message)
        self.error_type = error_type
        self.status_code = status_code
        self.message = message
        self.body = body

    def __str__(self) -> str:
        return f"{self.error_type.value} ({self.status_code}): {self.message}"
