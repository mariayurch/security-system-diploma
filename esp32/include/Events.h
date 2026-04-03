#pragma once

#include <Arduino.h>

void publishEvent(const char* sensor, const char* sensorId, const char* event);
void publishStatus(const char* event);
void publishHeartbeatIfDue();
void publishBootSequence();
void buildJsonPayload(char* payload, size_t payloadSize, const char* sensor, const char* sensorId, const char* event);