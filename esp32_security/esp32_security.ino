#include "secrets.h"
#include <WiFi.h>
#include <PubSubClient.h>

// ------------------------
// Pins
// ------------------------
#define DOOR_PIN 4
#define PIR_PIN 5
#define DOOR_TAMPER_PIN 18
#define MOTION_TAMPER_PIN 19
#define PANIC_BUTTON_PIN 33
#define ARM_BUTTON_PIN 26
#define RED_LED 27
#define GREEN_LED 25
#define BUZZER_PIN 21

// ------------------------
// Wi‑Fi / MQTT
// ------------------------
const char* ssid = WIFI_SSID;
const char* password = WIFI_PASSWORD;
const char* mqttServer = "broker.hivemq.com";
const int mqttPort = 1883;
const char* mqttClientId = "esp32_security_demo_01";

const char* eventsTopic = "home/security/events";
const char* statusTopic = "home/security/status";

WiFiClient espClient;
PubSubClient client(espClient);

// ------------------------
// Device metadata
// ------------------------
const char* deviceId = "esp32-1";
const char* zone = "room1";

// ------------------------
// Timing / debounce
// ------------------------
const unsigned long SENSOR_DEBOUNCE_MS = 80;
const unsigned long BUTTON_DEBOUNCE_MS = 120;
const unsigned long WIFI_RETRY_MS = 500;
const unsigned long MQTT_RETRY_MS = 2000;
const unsigned long HEARTBEAT_MS = 15000;

// ------------------------
// System state
// ------------------------
bool systemArmed = false;
bool alarmActive = false;
unsigned long eventCounter = 0;

int stableDoorState;
int lastRawDoorState;
unsigned long lastDoorChangeMs = 0;

int stableMotionState;
int lastRawMotionState;
unsigned long lastMotionChangeMs = 0;

int stableDoorTamperState;
int lastRawDoorTamperState;
unsigned long lastDoorTamperChangeMs = 0;

int stableMotionTamperState;
int lastRawMotionTamperState;
unsigned long lastMotionTamperChangeMs = 0;

int stableArmButtonState;
int lastRawArmButtonState;
unsigned long lastArmButtonChangeMs = 0;

int stablePanicButtonState;
int lastRawPanicButtonState;
unsigned long lastPanicButtonChangeMs = 0;

// ------------------------
// Siren state (non-blocking)
// ------------------------
int sirenFreq = 1500;
bool sirenUp = true;
unsigned long lastSirenStepMs = 0;
const unsigned long SIREN_STEP_MS = 20;

// ------------------------
// Heartbeat / reconnect control
// ------------------------
unsigned long lastHeartbeatMs = 0;
unsigned long lastMqttAttemptMs = 0;

// ------------------------
// Helpers
// ------------------------
void updateIndicators() {
  digitalWrite(RED_LED, systemArmed ? HIGH : LOW);
  digitalWrite(GREEN_LED, systemArmed ? LOW : HIGH);
}

void startAlarm() {
  alarmActive = true;
}

void stopAlarm() {
  alarmActive = false;
  noTone(BUZZER_PIN);
  sirenFreq = 1500;
  sirenUp = true;
}

void beepOnce(int frequency, int durationMs) {
  tone(BUZZER_PIN, frequency);
  delay(durationMs);
  noTone(BUZZER_PIN);
}

void beepArmConfirm() {
  if (alarmActive) return;

  for (int i = 0; i < 2; i++) {
    beepOnce(2000, 90);
    delay(80);
  }
}

void beepDisarmConfirm() {
  beepOnce(1700, 120);
  delay(80);
  beepOnce(1400, 140);
}

void buildJsonPayload(char* payload, size_t payloadSize, const char* sensor, const char* event) {
  long rssi = (WiFi.status() == WL_CONNECTED) ? WiFi.RSSI() : 0;
  unsigned long currentEventId = ++eventCounter;

  snprintf(
    payload,
    payloadSize,
    "{\"eventId\":%lu,\"deviceId\":\"%s\",\"zone\":\"%s\",\"sensor\":\"%s\",\"event\":\"%s\",\"armed\":%s,\"rssi\":%ld,\"ts\":%lu}",
    currentEventId,
    deviceId,
    zone,
    sensor,
    event,
    systemArmed ? "true" : "false",
    rssi,
    millis()
  );
}

void publishJson(const char* topic, const char* sensor, const char* event, bool retained = false) {
  char payload[320];
  buildJsonPayload(payload, sizeof(payload), sensor, event);

  Serial.print("Publishing to ");
  Serial.print(topic);
  Serial.print(": ");
  Serial.println(payload);

  client.publish(topic, payload, retained);
}

void publishEvent(const char* sensor, const char* event) {
  publishJson(eventsTopic, sensor, event, false);
}

void publishStatus(const char* event) {
  publishJson(statusTopic, "system", event, true);
}

void setupWifi() {
  Serial.println("Connecting to Wi‑Fi...");
  WiFi.begin(ssid, password);

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
  if (WiFi.status() == WL_CONNECTED) return;

  Serial.println("Wi‑Fi lost, reconnecting...");
  WiFi.disconnect();
  WiFi.begin(ssid, password);

  while (WiFi.status() != WL_CONNECTED) {
    delay(WIFI_RETRY_MS);
    Serial.print(".");
  }

  Serial.println();
  Serial.println("Wi‑Fi restored");
}

bool connectMqtt() {
  char willPayload[320];
  long rssi = (WiFi.status() == WL_CONNECTED) ? WiFi.RSSI() : 0;
  unsigned long currentEventId = ++eventCounter;

  snprintf(
    willPayload,
    sizeof(willPayload),
    "{\"eventId\":%lu,\"deviceId\":\"%s\",\"zone\":\"%s\",\"sensor\":\"system\",\"event\":\"connection_lost\",\"armed\":%s,\"rssi\":%ld,\"ts\":%lu}",
    currentEventId,
    deviceId,
    zone,
    systemArmed ? "true" : "false",
    rssi,
    millis()
  );

  Serial.println("Connecting to MQTT...");

  bool ok = client.connect(
    mqttClientId,
    statusTopic,
    1,
    true,
    willPayload
  );

  if (ok) {
    Serial.println("MQTT connected");
    publishEvent("system", "boot");
    publishEvent("system", "connection_restored");
    publishStatus("online");
    return true;
  }

  Serial.print("MQTT connect failed, rc=");
  Serial.println(client.state());
  return false;
}

void ensureMqtt() {
  if (client.connected()) return;

  unsigned long now = millis();
  if (now - lastMqttAttemptMs < MQTT_RETRY_MS) return;
  lastMqttAttemptMs = now;

  ensureWifi();
  connectMqtt();
}

void updateSiren() {
  if (!alarmActive) return;

  unsigned long now = millis();
  if (now - lastSirenStepMs < SIREN_STEP_MS) return;
  lastSirenStepMs = now;

  tone(BUZZER_PIN, sirenFreq);

  if (sirenUp) {
    sirenFreq += 20;
    if (sirenFreq >= 2500) sirenUp = false;
  } else {
    sirenFreq -= 20;
    if (sirenFreq <= 1500) sirenUp = true;
  }
}

void publishHeartbeat() {
  unsigned long now = millis();
  if (now - lastHeartbeatMs < HEARTBEAT_MS) return;
  lastHeartbeatMs = now;

  if (client.connected()) {
    publishEvent("system", "heartbeat");
  }
}

void handleArmButtonPress() {
  systemArmed = !systemArmed;
  updateIndicators();

  if (systemArmed) {
    Serial.println("SYSTEM ARMED");
    publishEvent("system", "armed");
    beepArmConfirm();
  } else {
    Serial.println("SYSTEM DISARMED");
    publishEvent("system", "disarmed");

    bool wasAlarmActive = alarmActive;
    stopAlarm();

    if (wasAlarmActive) {
      beepDisarmConfirm();
    } else {
      beepArmConfirm();
    }
  }
}

void handlePanicButtonPress() {
  Serial.println("PANIC BUTTON PRESSED");
  publishEvent("panic_button", "pressed");
  startAlarm();
}

void handleDoorStableChange(int newState) {
  if (!systemArmed) return;

  if (newState == LOW) {
    Serial.println("DOOR CLOSED");
    publishEvent("door", "closed");
  } else {
    Serial.println("DOOR OPEN");
    publishEvent("door", "open");
  }

  startAlarm();
}

void handleMotionStableChange(int newState) {
  if (!systemArmed) return;

  if (newState == HIGH) {
    Serial.println("MOTION DETECTED");
    publishEvent("motion", "detected");
    startAlarm();
  } else {
    Serial.println("MOTION RESTORED");
    publishEvent("motion", "restored");
  }
}

void handleDoorTamperStableChange(int newState) {
  if (!systemArmed) return;

  if (newState == LOW) {
    Serial.println("DOOR TAMPER RESTORED");
    publishEvent("door_tamper", "restored");
  } else {
    Serial.println("DOOR TAMPER TRIGGERED");
    publishEvent("door_tamper", "triggered");
    startAlarm();
  }
}

void handleMotionTamperStableChange(int newState) {
  if (!systemArmed) return;

  if (newState == LOW) {
    Serial.println("MOTION TAMPER RESTORED");
    publishEvent("motion_tamper", "restored");
  } else {
    Serial.println("MOTION TAMPER TRIGGERED");
    publishEvent("motion_tamper", "triggered");
    startAlarm();
  }
}

void updateDebouncedInput(
  int rawState,
  int &lastRawState,
  int &stableState,
  unsigned long &lastChangeMs,
  unsigned long debounceMs,
  void (*onStableChange)(int)
) {
  unsigned long now = millis();

  if (rawState != lastRawState) {
    lastRawState = rawState;
    lastChangeMs = now;
  }

  if ((now - lastChangeMs) >= debounceMs && rawState != stableState) {
    stableState = rawState;
    onStableChange(stableState);
  }
}

void onArmButtonStableChange(int state) {
  if (state == LOW) {
    handleArmButtonPress();
  }
}

void onPanicButtonStableChange(int state) {
  if (state == LOW) {
    handlePanicButtonPress();
  }
}

void setup() {
  Serial.begin(115200);

  pinMode(DOOR_PIN, INPUT_PULLUP);
  pinMode(PIR_PIN, INPUT);
  pinMode(DOOR_TAMPER_PIN, INPUT_PULLUP);
  pinMode(MOTION_TAMPER_PIN, INPUT_PULLUP);
  pinMode(PANIC_BUTTON_PIN, INPUT_PULLUP);
  pinMode(ARM_BUTTON_PIN, INPUT_PULLUP);

  pinMode(RED_LED, OUTPUT);
  pinMode(GREEN_LED, OUTPUT);
  pinMode(BUZZER_PIN, OUTPUT);

  updateIndicators();

  stableDoorState = lastRawDoorState = digitalRead(DOOR_PIN);
  stableMotionState = lastRawMotionState = digitalRead(PIR_PIN);
  stableDoorTamperState = lastRawDoorTamperState = digitalRead(DOOR_TAMPER_PIN);
  stableMotionTamperState = lastRawMotionTamperState = digitalRead(MOTION_TAMPER_PIN);
  stableArmButtonState = lastRawArmButtonState = digitalRead(ARM_BUTTON_PIN);
  stablePanicButtonState = lastRawPanicButtonState = digitalRead(PANIC_BUTTON_PIN);

  setupWifi();
  client.setServer(mqttServer, mqttPort);
  client.setKeepAlive(5);
  connectMqtt();
}

void loop() {
  ensureMqtt();
  client.loop();

  updateSiren();
  publishHeartbeat();

  updateDebouncedInput(
    digitalRead(ARM_BUTTON_PIN),
    lastRawArmButtonState,
    stableArmButtonState,
    lastArmButtonChangeMs,
    BUTTON_DEBOUNCE_MS,
    onArmButtonStableChange
  );

  updateDebouncedInput(
    digitalRead(PANIC_BUTTON_PIN),
    lastRawPanicButtonState,
    stablePanicButtonState,
    lastPanicButtonChangeMs,
    BUTTON_DEBOUNCE_MS,
    onPanicButtonStableChange
  );

  updateDebouncedInput(
    digitalRead(DOOR_PIN),
    lastRawDoorState,
    stableDoorState,
    lastDoorChangeMs,
    SENSOR_DEBOUNCE_MS,
    handleDoorStableChange
  );

  updateDebouncedInput(
    digitalRead(PIR_PIN),
    lastRawMotionState,
    stableMotionState,
    lastMotionChangeMs,
    SENSOR_DEBOUNCE_MS,
    handleMotionStableChange
  );

  updateDebouncedInput(
    digitalRead(DOOR_TAMPER_PIN),
    lastRawDoorTamperState,
    stableDoorTamperState,
    lastDoorTamperChangeMs,
    SENSOR_DEBOUNCE_MS,
    handleDoorTamperStableChange
  );

  updateDebouncedInput(
    digitalRead(MOTION_TAMPER_PIN),
    lastRawMotionTamperState,
    stableMotionTamperState,
    lastMotionTamperChangeMs,
    SENSOR_DEBOUNCE_MS,
    handleMotionTamperStableChange
  );

  delay(5);
}