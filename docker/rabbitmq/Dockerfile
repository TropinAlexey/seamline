# RabbitMQ with the delayed-message-exchange plugin enabled (ADR-0017) —
# not bundled in the stock rabbitmq:*-management image, but required for
# MassTransit's UseDelayedMessageScheduler() to work on this transport
# (the credit-approval saga's timeout, ADR-0008, depends on it).
FROM rabbitmq:3.13-management

ADD https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v3.13.0/rabbitmq_delayed_message_exchange-3.13.0.ez /opt/rabbitmq/plugins/

# ADD from a URL lands as root:root, mode 600 — unreadable by the rabbitmq
# user the entrypoint drops privileges to, so fix ownership before enabling.
RUN chown rabbitmq:rabbitmq /opt/rabbitmq/plugins/rabbitmq_delayed_message_exchange-3.13.0.ez \
    && chmod 644 /opt/rabbitmq/plugins/rabbitmq_delayed_message_exchange-3.13.0.ez \
    && rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange
