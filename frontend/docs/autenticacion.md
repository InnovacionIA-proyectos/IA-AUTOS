# Autenticación de la web

## Cómo funciona hoy

1. `/login` envía correo y contraseña a `POST /api/auth/login`. La API responde con un JWT y los datos del usuario.
2. El token se guarda en memoria (signal) y se refleja en `sessionStorage` para sobrevivir a una recarga de la pestaña.
   No se usa `localStorage`: al cerrar la pestaña la sesión desaparece.
3. El interceptor añade `Authorization: Bearer` solo a las llamadas a la API configurada (`apiUrl`), nunca a otros
   orígenes.
4. Un `401` con sesión activa cierra la sesión y lleva a `/login?motivo=expirada`. Otros errores se convierten en
   `ErrorApi` con el cuerpo uniforme del backend (`error`, `code`, `message`, `timestamp`, `details`).
5. Tras `inactividadMinutos` sin actividad (puntero, teclado, rueda del ratón, toque) se cierra la sesión
   (`/login?motivo=inactividad`).
6. El parámetro `redirigir` del login solo acepta rutas internas (empiezan por `/` y no por `//`), para evitar
   redirecciones abiertas.

Roles: la web admite `admin` y `super_admin`; `/usuarios` solo `super_admin`. Los guards (`guardAutenticado`, `guardRol`,
`guardInvitado`) solo ocultan pantallas: el rol que cuenta es el que la API lee de la base de datos en cada petición.

## Cómo cambiar a SSO

El login está detrás de una abstracción para poder sustituirlo sin tocar el resto de la aplicación:

```ts
// core/auth/proveedor-autenticacion.ts
export interface ProveedorAutenticacion {
  readonly tipo: ProveedorLogin;
  iniciarSesion(credenciales: Credenciales): Promise<Sesion>;
  cerrarSesion(): Promise<void>;
}
export const PROVEEDOR_AUTENTICACION = new InjectionToken<ProveedorAutenticacion>('PROVEEDOR_AUTENTICACION', { ... });
```

- `ProveedorLoginPropio` (activo con `proveedorLogin: "propio"`) usa la API del backend.
- `ProveedorSsoNoConfigurado` es un marcador: con `proveedorLogin: "sso"` lanza un error claro al iniciar sesión. Para
  implementarlo hay que decidir antes el flujo (OIDC con código y PKCE, o redirección a un portal corporativo) y cómo
  el backend valida el token emitido (nuevo esquema de autenticación JWT o intercambio por un token propio).
- El proveedor se elige en el arranque a partir de `config.json` (fábrica del token); el resto de la web (guards,
  interceptores, inactividad, cabecera) solo depende de `SesionService` y de `AuthService`.

Cuando se conozcan los requisitos del login corporativo, el trabajo se limita a: sustituir `ProveedorSsoNoConfigurado`
por una implementación real, ajustar la validación del token en el backend y cambiar `PROVEEDOR_LOGIN=sso` en el
despliegue.
