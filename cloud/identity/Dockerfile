FROM quay.io/keycloak/keycloak:26.8.0@sha256:27b3230fbabb8c9ff3c66c864cf09dcd8a41aca82e5100679d109ef0eeb05741 AS builder
COPY themes/focusguard /opt/keycloak/themes/focusguard
ENV KC_DB=postgres KC_HEALTH_ENABLED=true KC_METRICS_ENABLED=false
RUN /opt/keycloak/bin/kc.sh build --features=stateless

FROM quay.io/keycloak/keycloak:26.8.0@sha256:27b3230fbabb8c9ff3c66c864cf09dcd8a41aca82e5100679d109ef0eeb05741
COPY --from=builder /opt/keycloak/ /opt/keycloak/
COPY --chown=keycloak:keycloak themes/focusguard /opt/keycloak/themes/focusguard
COPY --chown=keycloak:keycloak realm-template.json /opt/keycloak/data/import/focusguard-realm.json
ENV KC_DB=postgres KC_HEALTH_ENABLED=true KC_METRICS_ENABLED=false \
    KC_HTTP_ENABLED=true KC_HTTP_PORT=8080 KC_HOSTNAME_STRICT=true \
    KC_PROXY_HEADERS=xforwarded KC_CACHE=local KC_FEATURES=stateless \
    KC_DB_POOL_INITIAL_SIZE=1 KC_DB_POOL_MIN_SIZE=0 KC_DB_POOL_MAX_SIZE=4
ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
CMD ["start", "--optimized", "--import-realm"]
