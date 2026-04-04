# Security Monitoring System (Diploma)

ESP32-based security monitoring and incident correlation system developed as a diploma project.

The project consists of two main parts:

- **ESP32 firmware** that reads sensors, applies debounce filtering, controls local indication / siren logic, and publishes telemetry via MQTT
- **ASP.NET Core backend** that receives events, stores them in PostgreSQL, correlates them in time windows, and forms security incidents

## Implemented functionality

### ESP32 firmware

- arm / disarm mode
- door sensor events
- motion sensor (PIR) events
- tamper events
- panic button handling
- local buzzer / siren signaling
- LED indication
- MQTT event publishing in JSON format
- boot event
- heartbeat event
- RSSI telemetry
- connection monitoring via MQTT status + Last Will

### Backend

- MQTT subscription to `home/security/events` and `home/security/status`
- JSON event ingestion
- raw event journaling in PostgreSQL
- duplicate event protection
- incident journaling
- time-window-based event correlation
- security incident formation with textual explanations
- REST API endpoints for events and incidents

## Implemented incident types

### Intrusion
Current logic includes:

- **Suspected intrusion**
  - first door open after arming
  - first motion detection after arming
- **Confirmed intrusion**
  - repeated trigger from the same sensor in the correlation window
  - or correlation between perimeter event and motion event

Additional rules:
- repeated confirmed motion events are limited by long cooldown logic
- repeated confirmed door events are limited within the current armed session

### Sabotage
Current logic includes:

- **Suspected sabotage**
  - tamper trigger while armed
  - connection loss while armed
- **Confirmed sabotage**
  - tamper + connection loss in the correlation window
  - multiple sabotage-related signals in the same armed session

### Panic
Current logic includes:

- immediate **confirmed** incident on panic button press
- duplicate suppression in a short time window

### Sensor Anomaly
Current logic includes:

- repeated identical events from the same sensor in a sliding time window
- sensor-specific thresholds
- dedicated anomaly handling for noisy motion behavior during long intrusion cooldown

## Architecture

```text
ESP32 → MQTT broker → ASP.NET Core backend → PostgreSQL
                                   └→ Telegram bot (planned)
