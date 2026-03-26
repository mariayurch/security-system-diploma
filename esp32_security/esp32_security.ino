#include <WiFi.h> 
#include <PubSubClient.h> 

#define DOOR_PIN 4 
#define PIR_PIN 5 
#define DOOR_TAMPER_PIN 18
#define MOTION_TAMPER_PIN 19
#define PANIC_BUTTON_PIN 33
#define ARM_BUTTON_PIN 26
#define RED_LED 27
#define GREEN_LED 25
#define BUZZER_PIN 21

const char* ssid = "Apple"; 
const char* password = "0502553777"; 

const char* mqtt_server = "broker.hivemq.com"; 

WiFiClient espClient; 
PubSubClient client(espClient); 

int lastDoor = HIGH; 
int lastMotion = LOW; 
int lastDoorTamper = HIGH;
int lastMotionTamper = HIGH;
int lastPanic = HIGH;
bool systemArmed = false;
int lastArmButton = HIGH;
bool alarmActive = false;
int freq = 1500;
bool up = true;

void setup_wifi() 
{ 
  delay(10); 
  Serial.println("Connecting to WiFi"); 
  WiFi.begin(ssid, password); 
  while (WiFi.status() != WL_CONNECTED) 
  { 
    delay(500); 
    Serial.print("."); 
  } 
  Serial.println("WiFi connected"); 
} 

void reconnect() 
  { 
    while (!client.connected()) { 
      Serial.println("Connecting to MQTT..."); 
      if (client.connect("esp32_security")) { 
        Serial.println("MQTT connected"); 
      } 
      else { 
        delay(2000); 
      } 
    } 
  }

  void publishEvent(const char* sensor, const char* event) { 

    char payload[200]; 
    snprintf(payload, sizeof(payload), "{\"device\":\"esp32\",\"sensor\":\"%s\",\"event\":\"%s\",\"timestamp\":%lu}", sensor, event, millis() ); 
    Serial.print("Publishing: "); 
    Serial.println(payload); 
    client.publish("home/security/events", payload); 

} 

void beepConfirm() {
  for (int i = 0; i < 2; i++) {
    tone(BUZZER_PIN, 2000);
    delay(100);
    noTone(BUZZER_PIN);
    delay(100);
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

  // стартовий стан (система знята)
  digitalWrite(GREEN_LED, HIGH);
  digitalWrite(RED_LED, LOW);

  setup_wifi(); 

  client.setServer(mqtt_server, 1883); 

} 

void loop() { 

  if (!client.connected()) { 
    reconnect(); 
  } 

  client.loop(); 

  int doorState = digitalRead(DOOR_PIN); 
  int motionState = digitalRead(PIR_PIN);

  int doorTamperState = digitalRead(DOOR_TAMPER_PIN);
  int motionTamperState = digitalRead(MOTION_TAMPER_PIN);

  int armButtonState = digitalRead(ARM_BUTTON_PIN);

  if (alarmActive) {

    tone(BUZZER_PIN, freq);

    if (up) {
      freq += 20;
      if (freq > 2500) up = false;
    } else {
      freq -= 20;
      if (freq < 1500) up = true;
    }

    delay(20);
  }

  if (armButtonState == LOW && lastArmButton == HIGH) {

    systemArmed = !systemArmed;

    if (systemArmed) {

      Serial.println("SYSTEM ARMED");
      publishEvent("system", "armed");

      digitalWrite(RED_LED, HIGH);
      digitalWrite(GREEN_LED, LOW);

      beepConfirm();

    } else {

      Serial.println("SYSTEM DISARMED");
      publishEvent("system", "disarmed");

      digitalWrite(RED_LED, LOW);
      digitalWrite(GREEN_LED, HIGH);

      alarmActive = false; // 🔴 ВАЖЛИВО: вимикаємо сирену

      beepConfirm();
    }

    delay(300);
  }

  lastArmButton = armButtonState;

  int panicState = digitalRead(PANIC_BUTTON_PIN);

  if (panicState == LOW && lastPanic == HIGH) {

    Serial.println("PANIC BUTTON PRESSED");
    publishEvent("panic_button", "pressed");

    alarmActive = true; // 🔥
  }

  lastPanic = panicState;

  if (systemArmed && doorState != lastDoor) { 

    if (doorState == LOW) { 
      Serial.println("DOOR CLOSED"); 
      publishEvent("door", "closed"); 
    } 
    else { 
      Serial.println("DOOR OPEN"); 
      publishEvent("door", "open"); 
    } 

    alarmActive = true; // 🔥

    lastDoor = doorState; 
  } 

  if (systemArmed && motionState == HIGH && lastMotion == LOW) { 
    Serial.println("MOTION DETECTED"); 
    publishEvent("motion", "detected"); 
    alarmActive = true; // 🔥
    lastMotion = HIGH; 
  } 

  if (motionState == LOW) { 
    lastMotion = LOW; 
  } 

  if (systemArmed && doorTamperState != lastDoorTamper) {

    if (doorTamperState == LOW) {

      Serial.println("DOOR TAMPER RESTORED");
      publishEvent("door_tamper", "restored");

    } else {

      Serial.println("DOOR TAMPER TRIGGERED");
      publishEvent("door_tamper", "triggered");

    }
    alarmActive = true; // 🔥
    lastDoorTamper = doorTamperState;
  }

  if (systemArmed && motionTamperState != lastMotionTamper) {

    if (motionTamperState == LOW) {

      Serial.println("MOTION TAMPER RESTORED");
      publishEvent("motion_tamper", "restored");

    } else {

      Serial.println("MOTION TAMPER TRIGGERED");
      publishEvent("motion_tamper", "triggered");

    }
    alarmActive = true; // 🔥
    lastMotionTamper = motionTamperState;
  }
      
  delay(50); 
}