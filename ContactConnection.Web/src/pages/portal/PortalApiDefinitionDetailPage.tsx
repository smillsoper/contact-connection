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
  deletePortalApiEndpoint,
  listPortalCredentials,
  setPortalCredential,
  testPortalAuth,
} from '../../api/portal'

const portalApi: DetailApi = {
  getDefinition: getPortalApiDefinition,
  updateDefinition: updatePortalApiDefinition,
  activateDefinition: activatePortalApiDefinition,
  deactivateDefinition: deactivatePortalApiDefinition,
  listEndpoints: listPortalApiEndpoints,
  createEndpoint: createPortalApiEndpoint,
  updateEndpoint: updatePortalApiEndpoint,
  deleteEndpoint: deletePortalApiEndpoint,
  listCredentials: () => listPortalCredentials().then((list) => list.map((c) => c.keyName)),
  setCredential: setPortalCredential,
  testAuth: testPortalAuth,
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
