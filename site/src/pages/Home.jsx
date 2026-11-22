import React from 'react';

const REPO = 'https://github.com/lucodifier/messaging-notifications';

const features = [
  ['📥', 'Transactional Outbox', 'Toda mensagem é gravada em SQLite antes de ir para a fila. Se o broker cair, nada se perde: o retry publica depois.'],
  ['🔁', 'Retry sem duplicar', 'Claim atômico, limite de tentativas e estado DEAD. O reenvio só vai para quem ainda não recebeu.'],
  ['🔑', 'Idempotência', 'Header Idempotency-Key por sistema: repetir o mesmo pedido devolve a mensagem já criada, não cria outra.'],
  ['🧭', 'Roteamento por tipo e origem', 'O tipo escolhe o canal (Email, SMS, WhatsApp) e o sistema de origem escolhe o provedor de email.'],
  ['📊', 'Status rastreável', 'O worker devolve SENT ou FAILED por correlationId, com publisher confirms. Consulta e auditoria paginada na API.'],
  ['🩺', 'Health checks de verdade', 'Liveness e readiness separados, acompanhando a conexão com o RabbitMQ, inclusive quedas e reconexões.']
];

const statuses = [
  ['PENDING', 'Gravada e publicada, aguardando o worker'],
  ['SENT', 'Entregue ao provedor'],
  ['FAILED', 'Falhou; o retry republica para quem falta'],
  ['DEAD', 'Esgotou as tentativas'],
  ['UNKNOWN', 'Sem resposta no prazo: não reenvia, pede verificação']
];

function goTo(id) {
  return (e) => {
    e.preventDefault();
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' });
  };
}

function toggleTheme() {
  const root = document.documentElement;
  const dark = root.dataset.theme
    ? root.dataset.theme === 'dark'
    : !window.matchMedia('(prefers-color-scheme: light)').matches;
  root.dataset.theme = dark ? 'light' : 'dark';
  try { localStorage.setItem('mn-theme', root.dataset.theme); } catch (e) { /* sem storage */ }
}

export default function Home() {
  return (
    <>
      <nav>
        <div className="wrap">
          <button className="logo" onClick={goTo('top')}><span>📨</span> Messaging Notifications</button>
          <div className="links">
            <button onClick={goTo('features')}>Recursos</button>
            <button onClick={goTo('how')}>Como funciona</button>
            <button onClick={goTo('run')}>Rodar</button>
            <button id="theme" aria-label="Alternar tema" onClick={toggleTheme}>◐</button>
            <a className="btn primary" href={REPO} target="_blank" rel="noopener">GitHub</a>
          </div>
        </div>
      </nav>

      <header className="hero" id="top">
        <div className="wrap">
          <span className="pill">.NET 8 · RabbitMQ · Docker</span>
          <h1>Notificações assíncronas,<br /><em>sem perder nem duplicar</em>.</h1>
          <p className="lead">
            Uma API recebe o pedido e responde na hora. Um worker consome a fila e envia por
            <b> Email</b>, <b>SMS</b> ou <b>WhatsApp</b>, conforme o tipo e os parâmetros da mensagem.
          </p>
          <div className="cta">
            <a className="btn primary" href={REPO} target="_blank" rel="noopener">Ver código no GitHub</a>
            <button className="btn" onClick={goTo('run')}>Rodar em 2 minutos</button>
          </div>
          <p className="meta">Sobe com Docker Compose · sem conta em provedor: emails caem no Mailpit</p>

          <div className="flow" aria-label="Fluxo: cliente, API, fila, worker e canais de envio">
            <div className="node"><small>cliente</small><b>POST /api/message</b></div>
            <i>→</i>
            <div className="node hl"><small>messaging-api</small><b>202 + correlationId</b></div>
            <i>→</i>
            <div className="node queue"><small>RabbitMQ</small><b>messages-queue</b></div>
            <i>→</i>
            <div className="node hl"><small>messaging-worker</small><b>escolhe o provedor</b></div>
            <i>→</i>
            <div className="node"><small>envio</small><b>Email · SMS · WhatsApp</b></div>
          </div>
          <p className="note">O worker devolve o resultado em <code>messages-status-queue</code> e a API atualiza o status da mensagem.</p>
        </div>
      </header>

      <section className="block" id="features">
        <div className="wrap">
          <span className="eyebrow">Recursos</span>
          <h2>O que um serviço de envio precisa garantir.</h2>
          <p className="sub">Aceitar rápido é fácil. O trabalho está em não perder mensagem quando algo cai e em não mandar duas vezes quando algo demora.</p>
          <div className="grid">
            {features.map(([icon, title, text]) => (
              <div className="card" key={title}>
                <div className="ic">{icon}</div>
                <h3>{title}</h3>
                <p>{text}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="block alt" id="how">
        <div className="wrap cols">
          <div>
            <span className="eyebrow">Como funciona</span>
            <h2>Um POST, um correlationId, um status.</h2>
            <p className="sub">
              O <code>notificationType</code> define o canal. O <code>metadata</code> leva os parâmetros do envio:
              assunto e template no email, texto no SMS e no WhatsApp. Campos extras viram variáveis do template.
            </p>
            <ul className="status">
              {statuses.map(([name, text]) => (
                <li key={name}><code>{name}</code><span>{text}</span></li>
              ))}
            </ul>
          </div>
          <pre>
<span className="d"># pedido</span>{'\n'}
<span className="p">POST</span> /api/message{'\n'}
Idempotency-Key: welcome-user-42{'\n\n'}
{'{'}{'\n'}
{'  '}<span className="k">"system"</span>: "demo-app",{'\n'}
{'  '}<span className="k">"subjectCode"</span>: "welcome-email",{'\n'}
{'  '}<span className="k">"notificationType"</span>: "Email",{'\n'}
{'  '}<span className="k">"recipients"</span>: ["user@example.com"],{'\n'}
{'  '}<span className="k">"metadata"</span>: {'{'}{'\n'}
{'    '}<span className="k">"subject"</span>: "Bem-vindo!",{'\n'}
{'    '}<span className="k">"template"</span>: "&lt;h1&gt;Olá, {'{{name}}'}!&lt;/h1&gt;",{'\n'}
{'    '}<span className="k">"name"</span>: "Maria"{'\n'}
{'  }'}{'\n'}
{'}'}{'\n\n'}
<span className="d"># resposta imediata</span>{'\n'}
<span className="p">202</span> {'{'} "correlationId": "66e5ba02…", "status": "PENDING" {'}'}{'\n\n'}
<span className="d"># depois</span>{'\n'}
<span className="p">GET</span> /api/message/66e5ba02…  →  "status": "SENT"
          </pre>
        </div>
      </section>

      <section className="block" id="projects">
        <div className="wrap">
          <span className="eyebrow">Projetos</span>
          <h2>Dois serviços, uma fila no meio.</h2>
          <div className="grid two">
            <a className="card link" href={`${REPO}/tree/main/messaging-api`} target="_blank" rel="noopener">
              <div className="ic">🚪</div>
              <h3>messaging-api</h3>
              <p>Minimal API. Valida o pedido, grava no Outbox, publica na fila e expõe status, auditoria e health checks. Swagger incluso.</p>
              <span className="more">Ver pasta →</span>
            </a>
            <a className="card link" href={`${REPO}/tree/main/messaging-worker`} target="_blank" rel="noopener">
              <div className="ic">⚙️</div>
              <h3>messaging-worker</h3>
              <p>Worker Service. Consome a fila, envia por SMTP, Elastic Email, Comtele ou Twilio e devolve o resultado na fila de status.</p>
              <span className="more">Ver pasta →</span>
            </a>
          </div>
        </div>
      </section>

      <section className="dl" id="run">
        <div className="wrap">
          <span className="eyebrow">Comece agora</span>
          <h2>Clone, copie o .env e suba.</h2>
          <div className="steps">
            <div className="card"><b>01</b><h3>API + RabbitMQ</h3>
              <pre className="cmd">cd messaging-api{'\n'}cp .env.example .env{'\n'}docker compose up -d --build</pre></div>
            <div className="card"><b>02</b><h3>Worker + Mailpit</h3>
              <pre className="cmd">cd ../messaging-worker{'\n'}cp .env.example .env{'\n'}docker compose up -d --build</pre></div>
            <div className="card"><b>03</b><h3>Envie e confira</h3>
              <p>Faça o POST pelo Swagger em <code>localhost:6006/swagger</code> e veja o email chegar em <code>localhost:8025</code>.</p></div>
          </div>
          <div className="cta">
            <a className="btn primary" href={REPO} target="_blank" rel="noopener">Abrir o repositório</a>
            <a className="btn" href={`${REPO}#readme`} target="_blank" rel="noopener">Ler o README</a>
          </div>
          <p className="meta">Também roda com <code>dotnet run</code>: copie <code>appsettings.example.json</code> para <code>appsettings.json</code>.</p>
        </div>
      </section>

      <footer>
        <div className="wrap">
          <span>© 2026 Messaging Notifications · feito por <a href="https://github.com/lucodifier" target="_blank" rel="noopener">lucodifier</a></span>
          <span><a href={REPO} target="_blank" rel="noopener">GitHub</a> · página feita com <a href="https://lucodifier.github.io/dachshund-landing/" target="_blank" rel="noopener">Dachshund</a></span>
        </div>
      </footer>
    </>
  );
}
