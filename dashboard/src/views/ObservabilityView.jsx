/**
 * Observability view: one card per telemetry service in the docker compose stack.
 *
 * These are external services (Grafana, Prometheus, Tempo, Loki, the OTLP collector) rather than
 * anything the node's REST API serves, so every card is a plain outbound link that opens in a new
 * tab. Grafana is the intended entry point; the backends without a browsable UI still get a card so
 * a reader who opens one is not surprised by a blank page.
 */

import React from 'react';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import PageHeader, { Card } from '@components/PageHeader.jsx';
import { ExternalIcon } from '@components/Icons.jsx';

/**
 * Host the observability services are reachable on.
 *
 * The compose stack publishes these ports on the same machine as the node, so we derive the host
 * from the endpoint the operator connected to — that way the links work when the console is opened
 * from another machine — and fall back to `localhost` when no usable host can be parsed.
 */
function observabilityHost(endpoint) {
  if (!endpoint) return 'localhost';
  try {
    const { hostname } = new URL(endpoint);
    return hostname || 'localhost';
  } catch {
    return 'localhost';
  }
}

export default function ObservabilityView() {
  const { t } = useTranslation();
  const { endpoint } = useApp();

  const host = observabilityHost(endpoint);
  const httpUrl = (port) => `http://${host}:${port}`;

  const services = [
    {
      key: 'grafana',
      name: 'Grafana',
      description: t('observability.grafanaDesc'),
      credentials: 'admin / admin',
      url: httpUrl(3001),
      primary: true,
    },
    {
      key: 'prometheus',
      name: 'Prometheus',
      description: t('observability.prometheusDesc'),
      credentials: null,
      url: httpUrl(9090),
    },
    {
      key: 'tempo',
      name: 'Tempo',
      description: t('observability.tempoDesc'),
      credentials: null,
      url: httpUrl(3200),
    },
    {
      key: 'loki',
      name: 'Loki',
      description: t('observability.lokiDesc'),
      credentials: null,
      url: httpUrl(3100),
    },
    {
      key: 'collector',
      name: 'OpenTelemetry Collector',
      description: t('observability.collectorDesc'),
      credentials: null,
      url: httpUrl(4318),
      grpcUrl: `grpc://${host}:4317`,
    },
  ];

  return (
    <div className="page">
      <PageHeader title={t('observability.title')} subtitle={t('observability.subtitle')} />

      <Card>
        <p className="card-padded observability-intro">{t('observability.intro')}</p>
      </Card>

      <div className="observability-grid">
        {services.map((service) => (
          <section className="card observability-card" key={service.key}>
            <div className="card-header">
              <div>
                {service.primary ? (
                  <div className="card-kicker">{t('observability.primaryTag')}</div>
                ) : null}
                <h2 className="card-title">{service.name}</h2>
                <p className="card-help">{service.description}</p>
              </div>
            </div>

            <div className="observability-card-body">
              <dl className="detail-grid is-compact">
                <dt>{t('observability.credentials')}</dt>
                <dd>{service.credentials ?? t('observability.noAuth')}</dd>
                <dt>{service.grpcUrl ? t('observability.urlHttp') : t('observability.url')}</dt>
                <dd>
                  <a
                    className="link-text"
                    href={service.url}
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    {service.url}
                  </a>
                </dd>
                {service.grpcUrl ? (
                  <>
                    <dt>{t('observability.urlGrpc')}</dt>
                    <dd>
                      <code>{service.grpcUrl}</code>
                    </dd>
                  </>
                ) : null}
              </dl>

              <a
                className="button-primary observability-open"
                href={service.url}
                target="_blank"
                rel="noopener noreferrer"
              >
                <ExternalIcon size={16} />
                {t('observability.open')}
              </a>
            </div>
          </section>
        ))}
      </div>
    </div>
  );
}
