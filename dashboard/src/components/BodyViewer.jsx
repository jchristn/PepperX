/**
 * Read-only viewer for an arbitrary HTTP body (JSON, XML, or plain text) with a pretty-print toggle.
 *
 * Nodes answer some surfaces in JSON and others (S3) in XML, and captured request/response bodies arrive
 * exactly as they went over the wire — often minified. This component detects the format and offers a
 * one-click switch between the raw bytes and an indented view, so an operator can read a wall-of-text
 * payload without leaving the console. JSON keeps the coloring from {@link JsonViewer}; XML and plain
 * text render in a monospace block. Formatting never mutates the source — the toggle only changes display.
 */

import React, { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';

import CopyButton from './CopyButton.jsx';
import JsonViewer from './JsonViewer.jsx';

/**
 * Classify a body as 'json', 'xml', 'text', or 'empty'. A content-type hint wins when present; otherwise
 * the first non-whitespace character disambiguates, and JSON is confirmed by an actual parse so a stray
 * leading brace in plain text is not mistaken for it.
 */
export function detectBodyKind(text, contentType = '') {
  const trimmed = (text || '').trim();
  if (!trimmed) return 'empty';

  const ct = (contentType || '').toLowerCase();
  if (ct.includes('json') || trimmed[0] === '{' || trimmed[0] === '[') {
    try {
      JSON.parse(trimmed);
      return 'json';
    } catch {
      // Not JSON after all; fall through to the other checks.
    }
  }
  if (ct.includes('xml') || trimmed[0] === '<') return 'xml';
  return 'text';
}

/** Indent a JSON document. Throws if the text is not valid JSON (callers guard with a try/catch). */
export function prettyJson(text) {
  return JSON.stringify(JSON.parse(text), null, 2);
}

/**
 * Indent an XML document without a parser: break between adjacent tags, then indent by nesting depth.
 * Self-closing tags, declarations/comments (`<?…?>`, `<!…>`), and elements whose text sits between their
 * own open and close tag on one line do not change the depth. Good enough for the S3 payloads the node
 * emits; malformed input degrades to reasonable-looking output rather than throwing.
 */
export function prettyXml(xml) {
  const withBreaks = (xml || '').replace(/\r\n/g, '\n').trim().replace(/>\s*</g, '>\n<');
  const unit = '  ';
  let depth = 0;
  const out = [];

  for (const raw of withBreaks.split('\n')) {
    const line = raw.trim();
    if (!line) continue;

    const isClosing = /^<\//.test(line);
    const isDeclaration = /^<[?!]/.test(line);
    const isSelfClosing = /\/>$/.test(line);
    const isSelfContained = /^<[^!?][^>]*>.*<\/[^>]+>$/.test(line);
    const isOpening = /^<[^!?/]/.test(line) && !isSelfClosing && !isSelfContained && !isDeclaration;

    if (isClosing) depth = Math.max(0, depth - 1);
    out.push(unit.repeat(depth) + line);
    if (isOpening) depth += 1;
  }

  return out.join('\n');
}

/**
 * Pretty-print a body according to its detected kind. Returns the input unchanged for plain text or when
 * formatting fails, so it is always safe to call — including on the value of an editable input.
 */
export function formatBody(text, contentType = '') {
  const kind = detectBodyKind(text, contentType);
  try {
    if (kind === 'json') return prettyJson(text);
    if (kind === 'xml') return prettyXml(text);
  } catch {
    // Fall through to the raw text.
  }
  return text;
}

/**
 * Render a body with a pretty/raw toggle. The toggle appears only when the body is formattable (JSON or
 * XML); plain text and empty bodies render without it. Defaults to the pretty view so payloads are
 * readable on arrival, matching the console's prior always-pretty JSON behavior.
 */
export default function BodyViewer({ body, contentType = '', emptyMessage = null, maxHeight = null }) {
  const { t } = useTranslation();
  const text = body ?? '';
  const kind = useMemo(() => detectBodyKind(text, contentType), [text, contentType]);
  const canFormat = kind === 'json' || kind === 'xml';
  const [pretty, setPretty] = useState(true);

  const prettyText = useMemo(() => (canFormat ? formatBody(text, contentType) : text), [text, contentType, canFormat]);

  if (!text) return <p className="muted">{emptyMessage || t('common.none')}</p>;

  const shown = pretty && canFormat ? prettyText : text;

  return (
    <div className="body-viewer">
      {canFormat ? (
        <div className="body-viewer-toolbar">
          <span className="body-viewer-kind">{kind.toUpperCase()}</span>
          <button
            type="button"
            className="body-viewer-toggle"
            aria-pressed={pretty}
            onClick={() => setPretty((value) => !value)}
          >
            {pretty ? t('common.rawText') : t('common.prettyPrint')}
          </button>
        </div>
      ) : null}

      {kind === 'json' && pretty ? (
        <JsonViewer value={shown} maxHeight={maxHeight} />
      ) : (
        <div className="json-viewer" style={maxHeight ? { maxHeight } : undefined}>
          <CopyButton value={shown} className="json-viewer-copy" />
          <pre>{shown}</pre>
        </div>
      )}
    </div>
  );
}
