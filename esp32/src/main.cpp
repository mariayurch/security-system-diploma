#include <Arduino.h>

#include <PubSubClient.h>
#include <WiFi.h>

#include "Config.h"
#include "Events.h"
#include "Inputs.h"
#include "State.h"
#include "Siren.h"
#include "WifiMqtt.h"

void setup() {
  Serial.begin(115200);

  systemState.bootId = String((uint32_t)esp_random(), HEX) + String((uint32_t)esp_random(), HEX);

  Serial.print("bootId: ");
  Serial.println(systemState.bootId);

  configureInputs();
  initializeInputStates();

  setupWifi();
  mqttClient.setServer(MQTT_SERVER, MQTT_PORT);
  mqttClient.setKeepAlive(5);
  connectMqtt();
}

void loop() {
  ensureMqtt();
  mqttClient.loop();

  updateSiren();
  publishHeartbeatIfDue();
  updateInputs();

  delay(MAIN_LOOP_DELAY_MS);
}