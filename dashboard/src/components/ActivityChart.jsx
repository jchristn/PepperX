/**
 * Stacked success/failure bar chart, drawn as SVG by hand.
 *
 * A charting library would be the larger part of the bundle for one chart. The two techniques worth
 * knowing: the bucket skeleton is generated client-side and the server's buckets are merged onto it
 * (so a window always renders the same bar count regardless of what the server returns), and the
 * SVG uses a fixed internal coordinate space with `width="100%"`, which makes it fluid without a
 * resize observer.
 */

import React, { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';

import useFormatters from '@hooks/useFormatters.js';
import AutoRefresh from './AutoRefresh.jsx';
import { RefreshIcon } from './Icons.jsx';

/** Selectable windows, with the bucket width each one asks the server for. */
export const TIME_RANGES = [
  { value: 'hour', labelKey: 'range.hour', hours: 1, stepMs: 60_000, bucketMinutes: 1 },
  { value: 'day', labelKey: 'range.day', hours: 24, stepMs: 900_000, bucketMinutes: 15 },
  { value: 'week', labelKey: 'range.week', hours: 168, stepMs: 3_600_000, bucketMinutes: 60 },
  { value: 'month', labelKey: 'range.month', hours: 720, stepMs: 21_600_000, bucketMinutes: 360 },
];

export function getTimeRange(value) {
  return TIME_RANGES.find((range) => range.value === value) ?? TIME_RANGES[1];
}

/** Start and end of a range, relative to now. */
export function rangeWindow(rangeId, now = Date.now()) {
  const range = getTimeRange(rangeId);
  return { from: new Date(now - range.hours * 3_600_000), to: new Date(now), range };
}

const WIDTH = 1000;
const HEIGHT = 220;
const PAD_TOP = 14;
const PAD_BOTTOM = 26;
const PAD_LEFT = 44;
const PAD_RIGHT = 14;
const PLOT_HEIGHT = HEIGHT - PAD_TOP - PAD_BOTTOM;
const PLOT_WIDTH = WIDTH - PAD_LEFT - PAD_RIGHT;

function floorToStep(milliseconds, stepMs) {
  return Math.floor(milliseconds / stepMs) * stepMs;
}

/** Zero-filled buckets covering the whole window. */
function generateBuckets(startMs, endMs, stepMs) {
  const buckets = [];
  for (let time = floorToStep(startMs, stepMs); time <= endMs; time += stepMs) {
    buckets.push({
      startMs: time,
      bucketStartUtc: new Date(time).toISOString(),
      bucketEndUtc: new Date(time + stepMs).toISOString(),
      successCount: 0,
      failureCount: 0,
      averageDurationMs: 0,
    });
  }
  return buckets;
}

/** Overlay whatever the server returned onto the skeleton. */
function mergeBuckets(skeleton, serverBuckets, stepMs) {
  if (!serverBuckets?.length) return skeleton;

  const index = new Map();
  for (const bucket of serverBuckets) {
    const start = new Date(bucket.BucketStartUtc ?? bucket.bucketStartUtc).getTime();
    if (Number.isNaN(start)) continue;
    index.set(floorToStep(start, stepMs), bucket);
  }

  return skeleton.map((bucket) => {
    const match = index.get(bucket.startMs);
    if (!match) return bucket;
    return {
      ...bucket,
      successCount: match.SuccessCount ?? match.successCount ?? 0,
      failureCount: match.FailureCount ?? match.failureCount ?? 0,
      averageDurationMs: match.AverageDurationMs ?? match.averageDurationMs ?? 0,
    };
  });
}

/** Round the axis maximum up to a whole number divisible into four ticks. */
function computeCeiling(maximum) {
  const step = Math.max(1, Math.ceil(maximum / 4));
  let ceiling = 0;
  while (ceiling < maximum) ceiling += step;
  return { ceiling: Math.max(ceiling, step), step };
}

export default function ActivityChart({
  summary,
  rangeId = 'day',
  onRangeChange = null,
  onBucketClick = null,
  onRefresh = null,
  autoRefreshKey = null,
  loading = false,
  title = null,
  showStats = true,
}) {
  const { t } = useTranslation();
  const formatters = useFormatters();
  const [hovered, setHovered] = useState(null);

  const range = getTimeRange(rangeId);

  const buckets = useMemo(() => {
    const end = Date.now();
    const start = end - range.hours * 3_600_000;
    const skeleton = generateBuckets(start, end, range.stepMs);
    const serverBuckets = summary?.Buckets ?? summary?.buckets ?? [];
    return mergeBuckets(skeleton, serverBuckets, range.stepMs);
  }, [summary, range.hours, range.stepMs]);

  const totals = useMemo(() => {
    let success = 0;
    let failure = 0;
    let durationSum = 0;
    let durationCount = 0;
    for (const bucket of buckets) {
      success += bucket.successCount;
      failure += bucket.failureCount;
      if (bucket.averageDurationMs > 0) {
        const count = bucket.successCount + bucket.failureCount;
        durationSum += bucket.averageDurationMs * count;
        durationCount += count;
      }
    }
    return {
      success,
      failure,
      total: success + failure,
      averageDuration: durationCount > 0 ? durationSum / durationCount : 0,
    };
  }, [buckets]);

  const maxCount = Math.max(1, ...buckets.map((bucket) => bucket.successCount + bucket.failureCount));
  const { ceiling, step } = computeCeiling(maxCount);
  const ticks = [];
  for (let value = 0; value <= ceiling; value += step) ticks.push(value);

  const groupWidth = PLOT_WIDTH / Math.max(1, buckets.length);
  const barWidth = Math.min(40, Math.max(2, groupWidth * 0.7));

  // Windows longer than two days need a date as well as a time, which roughly doubles label width.
  const compoundLabels = range.hours > 48;
  const axisLabel = (bucket) => {
    const date = new Date(bucket.startMs);
    return compoundLabels
      ? `${formatters.monthDay(date)} ${formatters.timeShort(date)}`
      : formatters.timeShort(date);
  };

  // Thin labels rather than letting them collide. The width is measured from a real rendered label
  // instead of guessed, because character count varies wildly across locales (a 12-hour English
  // clock is far wider than a 24-hour German one).
  const sampleLabel = buckets.length > 0 ? axisLabel(buckets[0]) : '';
  const estimatedLabelPx = sampleLabel.length * 6.2 + 16;
  const labelInterval = Math.max(
    1,
    Math.ceil(buckets.length / Math.max(1, Math.floor(PLOT_WIDTH / estimatedLabelPx))),
  );

  const hoveredBucket = hovered === null ? null : buckets[hovered];
  const empty = totals.total === 0;

  return (
    <div className="activity-chart">
      <div className="activity-chart-header">
        {title ? <h2 className="card-title">{title}</h2> : <span />}
        <div className="row">
          <div className="segmented" role="group" aria-label={t('home.activity')}>
            {TIME_RANGES.map((option) => (
              <button
                key={option.value}
                type="button"
                className={option.value === rangeId ? 'is-active' : undefined}
                onClick={() => onRangeChange?.(option.value)}
                aria-pressed={option.value === rangeId}
              >
                {t(option.labelKey)}
              </button>
            ))}
          </div>
          {onRefresh ? (
            <AutoRefresh onRefresh={onRefresh} storageKey={autoRefreshKey} disabled={loading} />
          ) : null}
          {onRefresh ? (
            <button
              type="button"
              className="button-icon"
              onClick={onRefresh}
              disabled={loading}
              title={t('common.refresh')}
              aria-label={t('common.refresh')}
            >
              <RefreshIcon size={16} />
            </button>
          ) : null}
        </div>
      </div>

      {showStats ? (
        <div className="activity-chart-stats">
          <div className="activity-stat">
            <span className="activity-stat-value">{formatters.number(totals.total)}</span>
            <span className="activity-stat-label">{t('chart.total')}</span>
          </div>
          <div className="activity-stat">
            <span className="activity-stat-value" style={{ color: 'var(--color-success)' }}>
              {formatters.number(totals.success)}
            </span>
            <span className="activity-stat-label">{t('chart.success')}</span>
          </div>
          <div className="activity-stat">
            <span className="activity-stat-value" style={{ color: 'var(--color-danger)' }}>
              {formatters.number(totals.failure)}
            </span>
            <span className="activity-stat-label">{t('chart.failure')}</span>
          </div>
          <div className="activity-stat">
            <span className="activity-stat-value">{formatters.duration(totals.averageDuration)}</span>
            <span className="activity-stat-label">{t('chart.avgDuration')}</span>
          </div>
        </div>
      ) : null}

      <div className="activity-chart-canvas">
        <svg
          width="100%"
          viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
          preserveAspectRatio="xMidYMid meet"
          style={{ display: 'block' }}
          role="img"
          aria-label={t('home.activity')}
        >
          {ticks.map((tick) => {
            const y = PAD_TOP + PLOT_HEIGHT - (tick / ceiling) * PLOT_HEIGHT;
            return (
              <g key={tick}>
                <line
                  x1={PAD_LEFT}
                  y1={y}
                  x2={WIDTH - PAD_RIGHT}
                  y2={y}
                  stroke="var(--color-chart-grid)"
                  strokeWidth="1"
                  strokeDasharray={tick === 0 ? 'none' : '4,4'}
                />
                <text x={PAD_LEFT - 6} y={y + 3} textAnchor="end" fontSize="10" fill="var(--color-text-muted)">
                  {formatters.number(tick)}
                </text>
              </g>
            );
          })}

          {buckets.map((bucket, index) => {
            const total = bucket.successCount + bucket.failureCount;
            const successHeight = (bucket.successCount / ceiling) * PLOT_HEIGHT;
            const failureHeight = (bucket.failureCount / ceiling) * PLOT_HEIGHT;
            const x = PAD_LEFT + index * groupWidth + (groupWidth - barWidth) / 2;

            return (
              <g
                key={bucket.startMs}
                onMouseEnter={() => setHovered(index)}
                onMouseLeave={() => setHovered(null)}
                onClick={onBucketClick && total > 0 ? () => onBucketClick(bucket) : undefined}
                style={{ cursor: onBucketClick && total > 0 ? 'pointer' : 'default' }}
              >
                {/* Full-column hit area so hovering the gaps and the axis strip still works. */}
                <rect
                  x={PAD_LEFT + index * groupWidth}
                  y={PAD_TOP}
                  width={groupWidth}
                  height={PLOT_HEIGHT + PAD_BOTTOM}
                  fill="transparent"
                />
                {bucket.failureCount > 0 ? (
                  <rect
                    x={x}
                    y={PAD_TOP + PLOT_HEIGHT - failureHeight}
                    width={barWidth}
                    height={failureHeight}
                    rx="2"
                    fill="var(--color-danger)"
                    opacity={hovered === index ? 1 : 0.85}
                  />
                ) : null}
                {bucket.successCount > 0 ? (
                  <rect
                    x={x}
                    y={PAD_TOP + PLOT_HEIGHT - successHeight - failureHeight}
                    width={barWidth}
                    height={successHeight}
                    rx="2"
                    fill="var(--color-success)"
                    opacity={hovered === index ? 1 : 0.85}
                  />
                ) : null}
                {index % labelInterval === 0 ? (
                  <text
                    x={PAD_LEFT + index * groupWidth + groupWidth / 2}
                    y={HEIGHT - 8}
                    textAnchor="middle"
                    fontSize="10"
                    fill="var(--color-text-muted)"
                  >
                    {axisLabel(bucket)}
                  </text>
                ) : null}
              </g>
            );
          })}
        </svg>

        {hoveredBucket ? (
          <div
            className="activity-chart-tooltip"
            style={{ left: `${((hovered + 0.5) / buckets.length) * 100}%` }}
          >
            <div className="activity-tooltip-time">{formatters.dateTimeShort(hoveredBucket.startMs)}</div>
            <div>
              {t('chart.success')}: {formatters.number(hoveredBucket.successCount)}
            </div>
            <div>
              {t('chart.failure')}: {formatters.number(hoveredBucket.failureCount)}
            </div>
            <div>
              {t('chart.total')}: {formatters.number(hoveredBucket.successCount + hoveredBucket.failureCount)}
            </div>
          </div>
        ) : null}

        {empty ? <div className="activity-chart-empty">{t('chart.noData')}</div> : null}
      </div>

      <div className="activity-chart-legend">
        <span className="legend-item">
          <span className="legend-swatch" style={{ background: 'var(--color-success)' }} />
          {t('chart.success')}
        </span>
        <span className="legend-item">
          <span className="legend-swatch" style={{ background: 'var(--color-danger)' }} />
          {t('chart.failure')}
        </span>
      </div>
    </div>
  );
}
