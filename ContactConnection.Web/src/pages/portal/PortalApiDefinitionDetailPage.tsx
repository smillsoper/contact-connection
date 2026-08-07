import { useParams } from 'react-router'
import PortalShell from '../../components/portal/PortalShell'
import ApiDefinitionDetailContent, { type DetailApi } from '../../components/apiDefinitions/ApiDefinitionDetailContent'
import {
  getPortalApiDefinition,
  updatePortalApiDefinition,
  activatePortalApiDefinition,
  deactivatePortalApiDefinition,
  listPortalApiEndpoints,
  createPortalApiEndpoint,
  updatePortalApiEndpoint,
  setPreferredPortalApiEndpoint,
  deletePortalApiEndpoint,
  listPortalCredentials,
  setPortalCredential,
  testPortalAuth,
  testPortalEndpoint,
  getPortalTtsProviders,
} from '../../api/portal'

const portalApi: DetailApi = {
  getDefinition: getPortalApiDefinition,
  updateDefinition: updatePortalApiDefinition,
  activateDefinition: activatePortalApiDefinition,
  deactivateDefinition: deactivatePortalApiDefinition,
  listEndpoints: listPortalApiEndpoints,
  createEndpoint: createPortalApiEndpoint,
  updateEndpoint: updatePortalApiEndpoint,
  setPreferred: setPreferredPortalApiEndpoint,
  deleteEndpoint: deletePortalApiEndpoint,
  listCredentials: () => listPortalCredentials().then((list) => list.map((c) => c.keyName)),
  setCredential: setPortalCredential,
  testAuth: testPortalAuth,
  testEndpoint: testPortalEndpoint,
  listTtsProviders: getPortalTtsProviders,
  listPagePath: '/portal/api-definitions',
}

export default function PortalApiDefinitionDetailPage() {
  const { id } = useParams<{ id: string }>()

  if (!id) return null

  return (
    <PortalShell>
      <ApiDefinitionDetailContent definitionId={id} api={portalApi} />
    </PortalShell>
  )
}
