# Dashboard móvil SoftRestaurant

PWA React que consume únicamente `/api/web/*`. No contiene fixtures, cifras de demostración
ni nombres de catálogo inventados.

## Desarrollo

Con la API ejecutándose en `http://localhost:5080`:

```powershell
npm install
npm run dev
```

Vite reenvía `/api` a la API local. Para validar la entrega:

```powershell
npm run lint
npm run build
```

## Producción

El Dockerfile compila la aplicación y la sirve mediante Nginx en el puerto `8080`. Nginx
reenvía `/api/*` al servicio `api`, por lo que la cookie de sesión permanece en el mismo
origen. El service worker guarda solamente el cascarón visual; nunca intercepta respuestas
de la API ni conserva cifras operativas.

La interfaz indica cuándo el día solicitado no tiene cobertura reconciliada o cuándo la
última sincronización está atrasada. Los importes no agregan un símbolo de moneda porque
la moneda de cada sucursal aún no forma parte del contrato sincronizado.
