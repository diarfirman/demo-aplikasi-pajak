import { useState } from 'react';

interface HelpStep {
  step: number;
  title: string;
  desc: string;
}

interface HelpNote {
  icon: string;
  text: string;
}

interface HelpPanelProps {
  title: string;
  steps?: HelpStep[];
  notes?: HelpNote[];
}

export default function HelpPanel({ title, steps, notes }: HelpPanelProps) {
  const [open, setOpen] = useState(false);

  return (
    <div className="help-panel">
      <button className="help-toggle" onClick={() => setOpen(!open)}>
        <span className="help-icon">?</span>
        <span>Petunjuk Penggunaan</span>
        <span className="help-chevron">{open ? '▲' : '▼'}</span>
      </button>

      {open && (
        <div className="help-body">
          <p className="help-title">{title}</p>

          {steps && steps.length > 0 && (
            <ol className="help-steps">
              {steps.map(s => (
                <li key={s.step}>
                  <strong>{s.title}</strong>
                  <span>{s.desc}</span>
                </li>
              ))}
            </ol>
          )}

          {notes && notes.length > 0 && (
            <ul className="help-notes">
              {notes.map((n, i) => (
                <li key={i}>
                  <span>{n.icon}</span>
                  <span>{n.text}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
