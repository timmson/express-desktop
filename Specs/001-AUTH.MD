#  Аутентификация
### 1. Проверка живой сессии - GET /api/v1/authentication/token/active

### 2. При первом запуске:
├── udid = Guid.NewGuid().randomUUID()
├── instanceId = Guid.NewGuid().randomUUID()
└── Сохранение в IndexedDB
    
### 3. Генерация Ed25519 ключей:
├── keyPair = crypto.subtle.generateKey('Ed25519')
├── sign_pub_key = export(publicKey)  // base64, 32 байта
├── sign_priv_key = export(privateKey) // base64, 64 байта
└── Сохранение в registrationIdKeys

### 4. Запрос QR-кода:
├── POST /api/v1/authentication/qr/mobile_to_web/request
├── Тело: { registration_id, registration_token, sign_pub_key, udid, ... }
├── Подпись: Ed25519(тело + nonce + digest)
└── Сервер → payload для QR-кода