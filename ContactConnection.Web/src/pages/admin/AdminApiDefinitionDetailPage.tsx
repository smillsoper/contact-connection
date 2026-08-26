import { useParams } from 'react-router'
import AdminShell from '../../components/admin/AdminShell'
import ApiDefinitionDetailContent, { type DetailApi } from '../../components/apiDefinitions/ApiDefinitionDetailContent'
import {
  getAdminApiDefinition,
  updateAdminApiDefinition,
  activateAdminApiDefinition,
  deactivateAdminApiDefinition,
  listAdminApiEndpoints,
  createAdminApiEndpoint,
  updateAdminApiEndpoint,
  setPreferredAdminApiEndpoint,
  deleteAdminApiEndpoint,
  testAdminAuth,
  testAdminEndpoint,
  listAdminApiDefinitionVersions,
  revertAdminApiDefinition,
  listAdminApiEndpointVersions,
  revertAdminApiEndpoint,
  listAdminTtsProviders,
} from '../../api/adminApiDefinitions'
import { listAdminCredentials, setAdminCredential } from '../../api/adminCredentials'

const adminApi: DetailApi = {
  getDefinition: getAdminApiDefinition,
  updateDefinition: updateAdminApiDefinition,
  activateDefinition: activateAdminApiDefinition,
  deactivateDefinition: deactivateAdminApiDefinition,
  listEndpoints: listAdminApiEndpoints,
  createEndpoint: createAdminApiEndpoint,
  updateEndpoint: updateAdminApiEndpoint,
  setPreferred: setPreferredAdminApiEndpoint,
  deleteEndpoint: deleteAdminApiEndpoint,
  listCredentials: () => listAdminCredentials().then((list) => list.map((c) => c.keyName)),
  setCredential: setAdminCredential,
  testAuth: testAdminAuth,
  testEndpoint: testAdminEndpoint,
  // Tenant admins can register their own TTS vendor account, subject to the same
  // TtsProviderValidation constraint as the platform catalog (see AdminApiEndpointsEndpoints) —
  // the provider picker applies here exactly as it does on the Portal side.
  listTtsProviders: () => listAdminTtsProviders().then((list) => list.map((p) => p.key)),
  listPagePath: '/admin/api-definitions',
  listDefinitionVersions: listAdminApiDefinitionVersions,
  revertDefinition: revertAdminApiDefinition,
  listEndpointVersions: listAdminApiEndpointVersions,
  revertEndpoint: revertAdminApiEndpoint,
}

export default function AdminApiDefinitionDetailPage() {
  const { id } = useParams<{ id: string }>()

  if (!id) return null

  return (
    <AdminShell>
      <ApiDefinitionDetailContent definitionId={id} api={adminApi} />
    </AdminShell>
  )
}
