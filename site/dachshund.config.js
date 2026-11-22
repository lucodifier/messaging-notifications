const title = 'Messaging Notifications — notificações assíncronas com .NET 8 e RabbitMQ';
const description =
  'API e worker em .NET 8 para enviar Email, SMS e WhatsApp por fila RabbitMQ, com Transactional Outbox, idempotência e retry sem envio duplicado.';

export default {
  global: {
    siteName: 'Messaging Notifications',
    baseUrl: 'https://lucodifier.github.io/messaging-notifications',
    lang: 'pt-BR'
  },
  routes: {
    '/': {
      seo: {
        title,
        description,
        keywords: 'dotnet, rabbitmq, outbox, notificações, email, sms, whatsapp, worker, mensageria',
        author: 'lucodifier',
        structuredData: {
          '@context': 'https://schema.org',
          '@type': 'SoftwareSourceCode',
          name: 'Messaging Notifications',
          description,
          programmingLanguage: 'C#',
          codeRepository: 'https://github.com/lucodifier/messaging-notifications',
          author: { '@type': 'Person', name: 'lucodifier', url: 'https://github.com/lucodifier' }
        }
      }
    }
  }
};
