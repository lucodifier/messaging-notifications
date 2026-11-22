import Dachshund from '@dachshund_framework/core';
import Home from './pages/Home';

const app = new Dachshund({
  baseUrl: 'https://lucodifier.github.io/messaging-notifications',
  scrollToTop: false
});

const seo = {
  title: 'Messaging Notifications — notificações assíncronas com .NET 8 e RabbitMQ',
  description:
    'API e worker em .NET 8 para enviar Email, SMS e WhatsApp por fila RabbitMQ, com Transactional Outbox, idempotência e retry sem envio duplicado.',
  keywords: 'dotnet, rabbitmq, outbox, notificações, email, sms, whatsapp, worker, mensageria'
};

// O roteador compara o pathname inteiro. No GitHub Pages o site vive em um
// subcaminho (/messaging-notifications/), então a home é registrada na raiz e
// também no caminho base, com e sem a barra final.
const base = import.meta.env.BASE_URL;
const homePaths = new Set(['/', base, base.replace(/\/$/, '') || '/']);
homePaths.forEach((path) => app.route(path, Home, seo));

export default app;
