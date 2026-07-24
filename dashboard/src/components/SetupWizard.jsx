/**
 * First-run guidance.
 *
 * Shown only when the node has no containers, because that is the one state where the console has
 * nothing to display and a new operator has no way to tell whether it is broken or simply empty.
 */

import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import Modal from './Modal.jsx';
import { ContainerIcon, SearchIcon, UploadIcon } from './Icons.jsx';

export default function SetupWizard() {
  const { t } = useTranslation();
  const { client, endpoint, setupDismissed, dismissSetup } = useApp();
  const navigate = useNavigate();

  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!client || setupDismissed) {
      setOpen(false);
      return;
    }

    let cancelled = false;
    client
      .statistics()
      .then((stats) => {
        if (!cancelled) setOpen((stats?.ContainerCount ?? 0) === 0);
      })
      .catch(() => {
        // An unreachable node is the topbar's problem to report, not the wizard's.
      });

    return () => {
      cancelled = true;
    };
  }, [client, endpoint, setupDismissed]);

  const steps = [
    { Icon: ContainerIcon, title: t('setup.step1'), hint: t('setup.step1Hint') },
    { Icon: UploadIcon, title: t('setup.step2'), hint: t('setup.step2Hint') },
    { Icon: SearchIcon, title: t('setup.step3'), hint: t('setup.step3Hint') },
  ];

  return (
    <Modal
      open={open}
      onClose={() => setOpen(false)}
      title={t('setup.title')}
      subtitle={t('setup.subtitle')}
      size="medium"
      footer={
        <>
          <button
            type="button"
            className="button-secondary"
            onClick={() => {
              dismissSetup(true);
              setOpen(false);
            }}
          >
            {t('setup.dismiss')}
          </button>
          <button
            type="button"
            className="button-primary"
            onClick={() => {
              dismissSetup(true);
              setOpen(false);
              navigate('/containers');
            }}
          >
            {t('setup.start')}
          </button>
        </>
      }
    >
      <ol className="setup-steps">
        {steps.map(({ Icon, title, hint }, index) => (
          <li key={title}>
            <span className="setup-step-index">{index + 1}</span>
            <Icon size={18} />
            <div>
              <p className="setup-step-title">{title}</p>
              <p className="muted">{hint}</p>
            </div>
          </li>
        ))}
      </ol>
    </Modal>
  );
}
