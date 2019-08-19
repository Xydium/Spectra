#include <FastLED.h>

#define LED_PIN 7
#define NUM_LEDS 60
#define BYTES_PER_STREAM NUM_LEDS * 60

CRGB leds[NUM_LEDS];
byte stream[BYTES_PER_STREAM];

void setup() {
  Serial.begin(230400);
  FastLED.addLeds<WS2812, LED_PIN, GRB>(leds, NUM_LEDS);
  delay(50);
}

void loop() {
  byte progress = 0;
  
  while(progress < BYTES_PER_STREAM) {
    while(Serial.available() > 0) {
      stream[progress] = Serial.read();
      progress++;
    }
  }

  for(byte i = 0; i < NUM_LEDS; i++) {
    leds[i] = CHSV(stream[i * 3], stream[i * 3 + 1], stream[i * 3 + 2]);
  }

  FastLED.show();
}
