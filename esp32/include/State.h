#pragma once

#include <PubSubClient.h>
#include <WiFi.h>
#include "Models.h"

extern WiFiClient wifiClient;
extern PubSubClient mqttClient;

extern SystemState systemState;
extern SirenState sirenState;

extern DebouncedInput armButtonInput;
extern DebouncedInput panicButtonInput;
extern DebouncedInput doorInput;
extern DebouncedInput motionInput;
extern DebouncedInput doorTamperInput;
extern DebouncedInput motionTamperInput;