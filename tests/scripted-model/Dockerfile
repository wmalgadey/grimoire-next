# The scripted model (ADR-0004) as an image, so the deployment suite (TS-18) can stand it where the
# model upstream stands — behind the egress proxy, off the hub's network. Test-only; never deployed.
# Built from tests/scripted-model/ after `npm run build`; it has no runtime dependencies.
FROM node:22-bookworm-slim
WORKDIR /model
COPY package.json ./
COPY dist/ dist/
ENV GRIMOIRE_SCRIPTED_MODEL_HOST=0.0.0.0 \
    GRIMOIRE_SCRIPTED_MODEL_PORT=8787
USER node
EXPOSE 8787
ENTRYPOINT ["node", "dist/server.js"]
