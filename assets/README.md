# Assets

| File | Size | Used by |
|---|---|---|
| `logo.png` | 314×314 RGBA | Repository README, Docker Hub (by absolute raw URL), dashboard connect screen and sidebar |
| `logo.ico` | 32×32 | Dashboard favicon |

The dashboard serves its own copies from `dashboard/public/`, because Vite only publishes what is
inside the app's public directory. **They are copies, not links** — replacing the logo means updating
both places:

```bash
cp assets/logo.png assets/logo.ico dashboard/public/
```

`DOCKERHUB_README.md` references `logo.png` by absolute
`raw.githubusercontent.com` URL, since Docker Hub cannot resolve repository-relative paths.

The artwork carries its own rounded background with transparent corners, so it needs no border
radius or backdrop from CSS — adding either clips or doubles up on the tile.
