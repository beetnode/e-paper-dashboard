import { describe, expect, it } from 'vitest';
import {
  CANVAS_ZOOM_STEPS,
  fitCanvasScale,
  parseCanvasZoom,
  resolveCanvasScale,
  stepCanvasZoom,
} from './designer-viewport';

describe('fitCanvasScale', () => {
  it('never upscales a canvas that already fits', () => {
    expect(fitCanvasScale({ width: 1200, height: 900 }, { width: 800, height: 480 })).toBe(1);
  });

  it('scales down to the narrower of width and height', () => {
    expect(fitCanvasScale({ width: 400, height: 900 }, { width: 800, height: 480 })).toBe(0.5);
    expect(fitCanvasScale({ width: 1600, height: 240 }, { width: 800, height: 480 })).toBe(0.5);
  });

  it('ignores height when it is unconstrained', () => {
    expect(fitCanvasScale({ width: 400, height: 0 }, { width: 800, height: 480 })).toBe(0.5);
  });

  it('keeps a minimum scale and tolerates unmeasured areas', () => {
    expect(fitCanvasScale({ width: 10, height: 10 }, { width: 1600, height: 1200 })).toBe(0.1);
    expect(fitCanvasScale({ width: 0, height: 0 }, { width: 800, height: 480 })).toBe(1);
  });
});

describe('resolveCanvasScale', () => {
  it('uses the fixed factor when zoom is not fit', () => {
    expect(resolveCanvasScale(1.5, { width: 100, height: 100 }, { width: 800, height: 480 })).toBe(1.5);
  });
});

describe('stepCanvasZoom', () => {
  it('moves to the neighbouring fixed step from an arbitrary fit scale', () => {
    expect(stepCanvasZoom(0.62, 1)).toBe(0.67);
    expect(stepCanvasZoom(0.62, -1)).toBe(0.5);
    expect(stepCanvasZoom(1, 1)).toBe(1.25);
  });

  it('clamps at both ends', () => {
    expect(stepCanvasZoom(2, 1)).toBe(CANVAS_ZOOM_STEPS[CANVAS_ZOOM_STEPS.length - 1]);
    expect(stepCanvasZoom(0.25, -1)).toBe(CANVAS_ZOOM_STEPS[0]);
  });
});

describe('parseCanvasZoom', () => {
  it('accepts fit and known steps only', () => {
    expect(parseCanvasZoom('fit')).toBe('fit');
    expect(parseCanvasZoom('0.5')).toBe(0.5);
    expect(parseCanvasZoom('0.42')).toBe('fit');
    expect(parseCanvasZoom(null)).toBe('fit');
  });
});
