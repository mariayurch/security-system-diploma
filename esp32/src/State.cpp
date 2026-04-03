#include "State.h"
#include "Config.h"

WiFiClient wifiClient;
PubSubClient mqttClient(wifiClient);

SystemState systemState;
SirenState sirenState;

DebouncedInput armButtonInput = {ARM_BUTTON_PIN, BUTTON_DEBOUNCE_MS, HIGH, HIGH, 0, nullptr};
DebouncedInput panicButtonInput = {PANIC_BUTTON_PIN, BUTTON_DEBOUNCE_MS, HIGH, HIGH, 0, nullptr};
DebouncedInput doorInput = {DOOR_PIN, SENSOR_DEBOUNCE_MS, HIGH, HIGH, 0, nullptr};
DebouncedInput motionInput = {PIR_PIN, SENSOR_DEBOUNCE_MS, LOW, LOW, 0, nullptr};
DebouncedInput doorTamperInput = {DOOR_TAMPER_PIN, SENSOR_DEBOUNCE_MS, HIGH, HIGH, 0, nullptr};
DebouncedInput motionTamperInput = {MOTION_TAMPER_PIN, SENSOR_DEBOUNCE_MS, HIGH, HIGH, 0, nullptr};