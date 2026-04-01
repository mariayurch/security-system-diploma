#include "WifiMqtt.h"
#include <Arduino.h>
#include "Config.h"
#include "Events.h"
#include "State.h"

static void buildWillPayload(char* payload, size_t payloadSize) {
  const unsigned long currentEventId = ++systemState.eventCounter;
  const long rssi = (WiFi.status() == WL_CONNECTED) ? WiFi.RSSI() : 0;

  snprintf(
    payload,
    payloadSize,
    "{\"bootId\":\"%s\",\"eventId\":%lu,\"deviceId\":\"%s\",\"zone\":\"%s\",\"sensor\":\"system\",\"event\":\"connection_lost\",\"armed\":%s,\"rssi\":%ld,\"ts\":%lu}",
    systemState.bootId.c_str(),
    currentEventId,
    DEVICE_ID,
    DEVICE_ZONE,
    systemState.armed ? "true" : "false",
    rssi,
    millis()
  );
}

void setupWifi() {
  Serial.println("Connecting to Wi‑Fi...");
  WiFi.begin(WIFI_SSID_VALUE, WIFI_PASSWORD_VALUE);

  while (WiFi.status() != WL_CONNECTED) {
    delay(WIFI_RETRY_MS);
    Serial.print(".");
  }

  Serial.println();
  Serial.println("Wi‑Fi connected");
  Serial.print("IP: ");
  Serial.println(WiFi.localIP());
}

void ensureWifi() {
  if (WiFi.status() == WL_CONNECTED) {
    return;
  }

  Serial.println("Wi‑Fi lost, reconnecting...");
  WiFi.disconnect();
  WiFi.begin(WIFI_SSID_VALUE, WIFI_PASSWORD_VALUE);

  while (WiFi.status() != WL_CONNECTED) {
    delay(WIFI_RETRY_MS);
    Serial.print(".");
  }

  Serial.println();
  Serial.println("Wi‑Fi restored");
}

bool connectMqtt() {
  char willPayload[512];
  buildWillPayload(willPayload, sizeof(willPayload));

  Serial.println("Connecting to MQTT...");

  const bool connected = mqttClient.connect(
    MQTT_CLIENT_ID,
    STATUS_TOPIC,
    1,
    false,
    willPayload
  );

  if (!connected) {
    Serial.print("MQTT connect failed, rc=");
    Serial.println(mqttClient.state());
    return false;
  }

  Serial.println("MQTT connected");
  publishBootSequence();
  return true;
}

void ensureMqtt() {
  if (mqttClient.connected()) {
    return;
  }

  const unsigned long now = millis();
  if (now - systemState.lastMqttAttemptMs < MQTT_RETRY_MS) {
    return;
  }

  systemState.lastMqttAttemptMs = now;
  ensureWifi();
  connectMqtt();
}
