#  Аутентификация
### 1. При первом запуске:
├── registration_id = Guid.NewGuid().randomUUID()
├── registration_token = Guid.NewGuid().randomUUID()
    
### 3. Генерация Ed25519 ключей:
├── keyPair = crypto.subtle.generateKey('Ed25519')
├── sign_pub_key = export(publicKey)  // base64, 32 байта
├── sign_priv_key = export(privateKey) // base64, 64 байта
└── Сохранение в registrationIdKeys

### 4. Генерация QR-кода:
├── Создание QR-кода с PayLoad: {"registration_id":"bbc61923-4625-5dc6-b910-758895807b22","registration_token":"XY+3FQayCDMLN5o3qjPZm+0l0soT18GoJAne970cNyGAq9MqZI7MXwpDGRO/3PsibEHq5RVy7LMLX08Q0wOitA==","registration_key":"6QkNRi352Ab07XEnIOXXYKHoCyxWcGJjyNOuB5DqPPI=","version":1}

### 5. Запрос QR-кода:
├── POST /api/v1/authentication/qr/mobile_to_web/request
├── Тело: { registration_id, registration_token, sign_pub_key, udid, ... }
├── Подпись: Ed25519(тело + nonce + digest)
└── Сервер → payload для QR-кода