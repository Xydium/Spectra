#include <FastLED.h>

#define LED_PIN 7
#define NUM_LEDS 60
#define BYTES_PER_STREAM 180
#define CHANNELS 3

CRGB leds[NUM_LEDS];
byte stream[BYTES_PER_STREAM];

byte progress = 0;
byte segment = 0;

void setup() {
  Serial.begin(230400);
  FastLED.addLeds<WS2812, LED_PIN, GRB>(leds, NUM_LEDS);
  delay(50);
}

void loop() {
  //Wait for any data
  if(Serial.available() > 0) {
    segment = Serial.read();
  
    if(segment > 2) {
      clearCurrent();
      return;
    }
  
    while(progress < NUM_LEDS) {
      if(Serial.available() > 0) {
        stream[segment + 3 * progress++] = Serial.read();
      }
    }

    progress = 0;
  
    if(segment == CHANNELS - 1) {
      segment = 0;
      
      for(byte i = 0; i < NUM_LEDS; i++) {
        leds[i] = CHSV(stream[i * 3], stream[i * 3 + 1], stream[i * 3 + 2]);
      }
  
      FastLED.show(); 
    }
  }
}

void clearCurrent() {
  byte current = Serial.available();
  for(byte i = 0; i < current; i++) {
    Serial.read();
  }
}

//New protocol:
//Wait for the next stream segment (0, 1, or 2)
//Write that segment into stream at channel * 60
//Confirm end character
//If segment mismatch, clear buffer, reset, and wait for next

//Alternatively:
//Write one pixel at a time with index specified. Increases data by 25%

//
//void loop() {
//  while(Serial.available() > 0 && progress < BYTES_PER_STREAM) {
//    stream[progress] = Serial.read();
//    progress++;
//  }
//
//  if(progress == BYTES_PER_STREAM) {
//    progress = 0;
//    
//    for(byte i = 0; i < NUM_LEDS; i++) {
//      leds[i] = CHSV(stream[i * 3], stream[i * 3 + 1], stream[i * 3 + 2]);
//    }
//
//    FastLED.show(); 
//  }
//}
