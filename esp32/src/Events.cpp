#include "Events.h"
#include "Config.h"
#include "State.h"

static long currentRssi() {
  return (WiFi.status() == WL_CONNECTED) ? WiFi.RSSI() : 0;
}

void buildJsonPayload(char* payload, size_t payloadSize, const char* sensor, const char* event) {
  const unsigned long currentEventId = ++systemState.eventCounter;

  snprintf(
    payload,
    payloadSize,
    "{\"bootId\":\"%s\",\"eventId\":%lu,\"deviceId\":\"%s\",\"zone\":\"%s\",\"sensor\":\"%s\",\"event\":\"%s\",\"armed\":%s,\"rssi\":%ld,\"ts\":%lu}",
    systemState.bootId.c_str(),
    currentEventId,
    DEVICE_ID,
    DEVICE_ZONE,
    sensor,
    event,
    systemState.armed ? "true" : "false",
    currentRssi(),
    millis()
  );
}

static void publishJson(const char* topic, const char* sensor, const char* event, bool retained) {
  char payload[512];
  buildJsonPayload(payload, sizeof(payload), sensor, event);

  Serial.print("Publishing to ");
  Serial.print(topic);
  Serial.print(": ");
  Serial.println(payload);

  mqttClient.publish(topic, payload, retained);
}

void publishEvent(const char* sensor, const char* event) {
  publishJson(EVENTS_TOPIC, sensor, event, false);
}

void publishStatus(const char* event) {
  publishJson(STATUS_TOPIC, "system", event, true);
}

void publishHeartbeatIfDue() {
  const unsigned long now = millis();
  if (now - systemState.lastHeartbeatMs < HEARTBEAT_MS) {
    return;
  }

  systemState.lastHeartbeatMs = now;

  if (mqttClient.connected()) {
    publishEvent("system", "heartbeat");
  }
}

void publishBootSequence() {
  publishEvent("system", "boot");
  publishEvent("system", "connection_restored");
  publishStatus("online");
}
