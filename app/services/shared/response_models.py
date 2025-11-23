"""Common response models for OCR services."""

from typing import List, Dict, Optional
from pydantic import BaseModel


class ColorInfo(BaseModel):
    """Color information for a text region."""
    rgb: List[int]
    hex: str
    percentage: float


class BackgroundForegroundColors(BaseModel):
    """Background and foreground color information."""
    background_color: ColorInfo
    foreground_color: ColorInfo
    color_source: str = "ugtlive_gpu_kmeans"


class TextObject(BaseModel):
    """OCR text object containing detected text and metadata."""
    text: str
    x: int
    y: int
    width: int
    height: int
    vertices: List[List[int]]
    confidence: Optional[float] = None
    background_color: Optional[ColorInfo] = None
    foreground_color: Optional[ColorInfo] = None
    color_source: Optional[str] = None
    text_orientation: Optional[str] = None


class OCRResponse(BaseModel):
    """Standard OCR response format."""
    status: str = "success"
    texts: List[TextObject]
    processing_time: float
    language: str
    char_level: bool
    backend: str = "cpu"


class ErrorResponse(BaseModel):
    """Standard error response format."""
    status: str = "error"
    message: str
    error_type: Optional[str] = None


class ServiceInfo(BaseModel):
    """Service information response."""
    service_name: str
    description: str
    version: str
    env_name: str
    port: int
    local_only: bool
    github_url: Optional[str] = None
    author: Optional[str] = None


class ShutdownResponse(BaseModel):
    """Shutdown confirmation response."""
    status: str = "success"
    message: str = "Service shutting down"

