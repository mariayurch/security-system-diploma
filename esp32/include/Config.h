#pragma once

#include "secrets.h"

// ------------------------
// Pins
// ------------------------
constexpr int DOOR_PIN = 4;
constexpr int PIR_PIN = 5;
constexpr int DOOR_TAMPER_PIN = 18;
constexpr int MOTION_TAMPER_PIN = 19;
constexpr int PANIC_BUTTON_PIN = 33;
constexpr int ARM_BUTTON_PIN = 26;
constexpr int RED_LED_PIN = 27;
constexpr int GREEN_LED_PIN = 25;
constexpr int BUZZER_PIN = 21;

constexpr uint8_t WIFI_TEST_BUTTON_PIN = 32;
constexpr unsigned long WIFI_TEST_BUTTON_DEBOUNCE_MS = 50;

// ------------------------
// Wi‑Fi / MQTT
// ------------------------
constexpr const char* WIFI_SSID_VALUE = WIFI_SSID;
constexpr const char* WIFI_PASSWORD_VALUE = WIFI_PASSWORD;
constexpr const char* MQTT_SERVER = "broker.hivemq.com";
constexpr int MQTT_PORT = 1883;
constexpr const char* MQTT_CLIENT_ID = "esp32_security_demo_01";

constexpr const char* EVENTS_TOPIC = "home/security/events";
constexpr const char* STATUS_TOPIC = "home/security/status";

// ------------------------
// Device metadata
// ------------------------
constexpr const char* DEVICE_ID = "esp32-1";
constexpr const char* DEVICE_ZONE = "room1";
constexpr const char* SYSTEM_SENSOR_ID = "system-1";
constexpr const char* DOOR_SENSOR_ID = "door-1";
constexpr const char* MOTION_SENSOR_ID = "motion-1";
constexpr const char* DOOR_TAMPER_SENSOR_ID = "door-tamper-1";
constexpr const char* MOTION_TAMPER_SENSOR_ID = "motion-tamper-1";
constexpr const char* PANIC_SENSOR_ID = "panic-1";

// ------------------------
// Timing / debounce
// ------------------------
constexpr unsigned long SENSOR_DEBOUNCE_MS = 80;
constexpr unsigned long BUTTON_DEBOUNCE_MS = 120;
constexpr unsigned long WIFI_RETRY_MS = 500;
constexpr unsigned long MQTT_RETRY_MS = 2000;
constexpr unsigned long HEARTBEAT_MS = 15000;
constexpr unsigned long MAIN_LOOP_DELAY_MS = 5;

// ------------------------
// Siren
// ------------------------
constexpr int SIREN_MIN_FREQ = 1500;
constexpr int SIREN_MAX_FREQ = 2500;
constexpr int SIREN_STEP_FREQ = 20;
constexpr unsigned long SIREN_STEP_MS = 20;
